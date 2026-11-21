namespace TiaFormat.Program;

/// <summary>
/// Writes a LAD or FBD network in statement form (ADR 0013): the logic of contacts and gates as AND, OR and NOT
/// expressions assigned to the coils they drive, set and reset coils as IF statements, boxes as assignments, and
/// instructions and calls as calls with their parameters. Parts are visited in network order, and every part, operand,
/// negation and connection the network stores appears; nothing else does. docs/FORMAT.md, section 13, has the rules.
/// </summary>
public static class NetworkStatements {
    /// <summary>The network's statements, one per line.</summary>
    public static IReadOnlyList<string> Write(TiaFlgNet net) => new Builder(net).Run();

    public static void Write(TiaFlgNet net, PooledByteWriter output) {
        var lines = Write(net);
        for (var i = 0; i < lines.Count; i++) {
            if (i != 0) {
                output.Write((byte)'\n');
            }
            output.Write(lines[i]);
        }
    }

    /// <summary>
    /// The condition a GRAPH network ends in: what drives its transition, supervision or interlock coil (<c>TrCoil</c>,
    /// <c>SvCoil</c>, <c>IlCoil</c>); TRUE for an empty one, whose coil hangs on the power rail.
    /// </summary>
    public static string Condition(TiaFlgNet net) {
        var coil = net.Parts.FirstOrDefault(part => part.Name is "TrCoil" or "SvCoil" or "IlCoil");
        return coil is null ? "TRUE" : new Builder(net).Condition(coil);
    }

    /// <summary>An expression and how tightly it binds, so that it is put in parentheses only where it must be.</summary>
    private sealed record Expr(string Text, int Precedence) {
        public const int Or = 1, Xor = 2, And = 3, Compare = 4, Add = 5, Multiply = 6, Not = 8, Atom = 9;

        public static readonly Expr True = new("TRUE", Atom);

        public bool IsTrue => Text == "TRUE";

        public string Within(int precedence) => Precedence < precedence ? "(" + Text + ")" : Text;
    }

    private static readonly Dictionary<string, string> Comparisons = new(StringComparer.Ordinal) {
        ["Eq"] = "=", ["Ne"] = "<>", ["Gt"] = ">", ["Ge"] = ">=", ["Lt"] = "<", ["Le"] = "<=",
    };

    private static readonly Dictionary<string, (string Operator, int Precedence)> Arithmetic = new(StringComparer.Ordinal) {
        ["Add"] = ("+", Expr.Add), ["Sub"] = ("-", Expr.Add), ["Mul"] = ("*", Expr.Multiply), ["Div"] = ("/", Expr.Multiply), ["Mod"] = ("MOD", Expr.Multiply),
    };

    /// <summary>The order an instruction's parameters are declared in, so a call lists them as TIA's help does.</summary>
    private static readonly Dictionary<string, string[]> InstructionPins = new(StringComparer.Ordinal) {
        ["TON"] = ["IN", "PT", "Q", "ET"], ["TOF"] = ["IN", "PT", "Q", "ET"], ["TP"] = ["IN", "PT", "Q", "ET"], ["TONR"] = ["IN", "R", "PT", "Q", "ET"],
        ["CTU"] = ["CU", "R", "PV", "Q", "CV"], ["CTD"] = ["CD", "LD", "PV", "Q", "CV"], ["CTUD"] = ["CU", "CD", "R", "LD", "PV", "QU", "QD", "CV"],
    };

    private sealed class Builder(TiaFlgNet net) {
        private readonly Dictionary<int, TiaFlgPart> _parts = net.Parts.ToDictionary(part => part.Uid);
        private readonly Dictionary<(int, string), Expr> _values = [];
        private readonly List<string> _lines = [];

        public List<string> Run() {
            foreach (var label in net.Labels) {
                _lines.Add(label + ":");
            }
            foreach (var part in net.Parts) {
                Emit(part);
            }
            return _lines;
        }

        public string Condition(TiaFlgPart coil) => Input(coil, "in").Text;

        private void Emit(TiaFlgPart part) {
            switch (part.Name) {
                case "Coil":
                    Assign(Operand(part, "operand"), part.Negated.Contains("operand") ? Not(Input(part, "in")) : Input(part, "in"));
                    return;
                case "SCoil" or "RCoil":
                    Conditional(Input(part, "in"), Operand(part, "operand") + (part.Name == "SCoil" ? " := TRUE;" : " := FALSE;"));
                    return;
                case "Sr":
                    Conditional(Input(part, "s"), Operand(part, "operand") + " := TRUE;");
                    Conditional(Input(part, "r1"), Operand(part, "operand") + " := FALSE;");
                    AssignOutputs(part, "q");
                    return;
                case "Rs":
                    Conditional(Input(part, "r"), Operand(part, "operand") + " := FALSE;");
                    Conditional(Input(part, "s1"), Operand(part, "operand") + " := TRUE;");
                    AssignOutputs(part, "q");
                    return;
                case "Jump" or "JumpNot":
                    var condition = Input(part, "in");
                    Conditional(part.Name == "Jump" ? condition : Not(condition), "GOTO " + Operand(part, "label") + ";");
                    return;
                case "Return":
                    Conditional(Input(part, "in"), "RETURN;");
                    return;
                case "Move":
                    Enabled(part, [.. OutputPins(part).Where(pin => pin.StartsWith("out", StringComparison.Ordinal))
                        .SelectMany(pin => Targets(part, pin)).Select(target => target + " := " + Input(part, "in").Text + ";")]);
                    return;
            }
            if (part.IsCall || part.IsInstruction || !IsKnownGate(part.Name)) {
                Call(part);
                return;
            }
            // A gate, comparison or arithmetic box: its value goes where its outputs are wired to an operand.
            if (Arithmetic.ContainsKey(part.Name)) {
                Enabled(part, [.. Targets(part, "out").Select(target => target + " := " + Value(part, "out").Text + ";")]);
                return;
            }
            AssignOutputs(part, "out");
        }

        private void AssignOutputs(TiaFlgPart part, string pin) {
            foreach (var target in Targets(part, pin)) {
                Assign(target, Value(part, pin));
            }
        }

        private void Assign(string target, Expr value) => _lines.Add(target + " := " + value.Text + ";");

        private void Conditional(Expr condition, string statement) =>
            _lines.Add(condition.IsTrue ? statement : "IF " + condition.Text + " THEN " + statement + " END_IF;");

        /// <summary>A box's statements, inside an IF when its enable input is wired to anything but the power rail.</summary>
        private void Enabled(TiaFlgPart part, List<string> statements) {
            if (statements.Count == 0) {
                return;
            }
            var enable = Driver(part, "en");
            if (enable is null || enable.IsTrue) {
                _lines.AddRange(statements);
                return;
            }
            _lines.Add("IF " + enable.Text + " THEN");
            _lines.AddRange(statements.Select(statement => "    " + statement));
            _lines.Add("END_IF;");
        }

        /// <summary>An instruction, a call of a block, or a part with no rule: a call naming every connected pin.</summary>
        private void Call(TiaFlgPart part) {
            var arguments = new List<string>();
            string? result = null;
            foreach (var pin in Pins(part)) {
                if (pin is "en" or "eno") {
                    continue;
                }
                if (IsOutput(part, pin)) {
                    var targets = Targets(part, pin);
                    if (part.IsCall && Section(part, pin) == "Return") {
                        result = targets.FirstOrDefault();
                        continue;
                    }
                    arguments.AddRange(targets.Select(target => pin + " => " + target));
                } else if (Driver(part, pin) is { } value) {
                    arguments.Add(pin + " := " + value.Text);
                }
            }
            var callee = part.Instance is { } instance
                ? (part.IsInstruction && instance.StartsWith('"') ? instance + "." + part.Name : instance)
                : part.Name;
            var statement = (result is null ? "" : result + " := ") + callee + "(" + string.Join(", ", arguments) + ");";
            Enabled(part, [statement]);
        }

        /// <summary>The value on one of a part's outputs, inverted when the pin is negated.</summary>
        private Expr Value(TiaFlgPart part, string pin) {
            if (_values.TryGetValue((part.Uid, pin), out var known)) {
                return known;
            }
            var value = Compute(part, pin);
            if (part.Negated.Contains(pin)) {
                value = Not(value);
            }
            _values[(part.Uid, pin)] = value;
            return value;
        }

        private Expr Compute(TiaFlgPart part, string pin) {
            if (pin is "eno" or "ENO") {
                return Driver(part, "en") ?? Expr.True;
            }
            switch (part.Name) {
                case "Contact":
                    var operand = new Expr(Operand(part, "operand"), Expr.Atom);
                    return And(Input(part, "in"), part.Negated.Contains("operand") ? Not(operand) : operand);
                case "PContact" or "NContact":
                    return And(Input(part, Pins(part).Contains("pre") ? "pre" : "in"), Edge(part.Name[0], Operand(part, "operand"), Operand(part, "bit")));
                case "PBox" or "NBox":
                    return Edge(part.Name[0], Input(part, "in").Text, Operand(part, "bit"));
                case "A" or "O" or "X":
                    var inputs = InputPins(part).Select(input => Input(part, input)).ToList();
                    return part.Name switch {
                        "A" => inputs.Aggregate(Expr.True, And),
                        "O" => Combine(inputs, " OR ", Expr.Or),
                        _ => Combine(inputs, " XOR ", Expr.Xor),
                    };
                case "Coil" or "SCoil" or "RCoil":
                    return Input(part, "in");
                case "Sr" or "Rs":
                    return new Expr(Operand(part, "operand"), Expr.Atom);
                case "Not":
                    return Not(Input(part, "in"));
                case "Move":
                    return Input(part, "in");
            }
            if (Comparisons.TryGetValue(part.Name, out var comparison)) {
                var compared = new Expr(Input(part, "in1").Within(Expr.Add) + " " + comparison + " " + Input(part, "in2").Within(Expr.Add), Expr.Compare);
                return Pins(part).Contains("pre") ? And(Input(part, "pre"), compared) : compared;
            }
            if (Arithmetic.TryGetValue(part.Name, out var arithmetic)) {
                var terms = InputPins(part).Select(input => Input(part, input)).ToList();
                return new Expr(string.Join(" " + arithmetic.Operator + " ", terms.Select((term, i) => term.Within(i == 0 ? arithmetic.Precedence : arithmetic.Precedence + 1))), arithmetic.Precedence);
            }
            if (part.Instance is { } instance) {
                return new Expr(instance + "." + pin, Expr.Atom);
            }
            return new Expr("P" + part.Uid + "." + pin, Expr.Atom);
        }

        private static Expr Edge(char kind, string signal, string memory) => new((kind == 'P' ? "P(" : "N(") + signal + ", " + memory + ")", Expr.Atom);

        /// <summary>What drives one of a part's inputs, inverted when the pin is negated; true when nothing does.</summary>
        private Expr Input(TiaFlgPart part, string pin) {
            var value = Driver(part, pin) ?? Expr.True;
            return part.Negated.Contains(pin) ? Not(value) : value;
        }

        /// <summary>What drives a pin: a part's output, the power rail or an operand on the same wire; null for none.</summary>
        private Expr? Driver(TiaFlgPart part, string pin) {
            foreach (var wire in WiresOf(part.Uid, pin)) {
                foreach (var end in wire) {
                    if (end.Kind == TiaFlgEndKind.Pin && end.Uid != part.Uid && _parts.TryGetValue(end.Uid, out var source) && IsOutput(source, end.Pin ?? "")) {
                        return Value(source, end.Pin ?? "");
                    }
                }
                foreach (var end in wire) {
                    if (end.Kind == TiaFlgEndKind.Powerrail) {
                        return Expr.True;
                    }
                    if (end.Kind == TiaFlgEndKind.Operand && net.Operands.GetValueOrDefault(end.Uid) is { } operand) {
                        return new Expr(operand, Expr.Atom);
                    }
                }
            }
            return null;
        }

        /// <summary>The operands an output pin is wired to.</summary>
        private List<string> Targets(TiaFlgPart part, string pin) =>
            [.. WiresOf(part.Uid, pin).SelectMany(wire => wire)
                .Where(end => end.Kind == TiaFlgEndKind.Operand && net.Operands.GetValueOrDefault(end.Uid) is not null)
                .Select(end => net.Operands[end.Uid]!)];

        /// <summary>The operand on a pin that names one, such as a coil's operand or a jump's label.</summary>
        private string Operand(TiaFlgPart part, string pin) => Driver(part, pin)?.Text ?? Targets(part, pin).FirstOrDefault() ?? "?";

        private IEnumerable<List<TiaFlgEnd>> WiresOf(int uid, string pin) =>
            net.Wires.Where(wire => wire.Any(end => end.Kind == TiaFlgEndKind.Pin && end.Uid == uid && end.Pin == pin));

        /// <summary>Every pin a wire reaches, in a call's declared order where it has one, else as the wires list them.</summary>
        private List<string> Pins(TiaFlgPart part) {
            var wired = net.Wires.SelectMany(wire => wire).Where(end => end.Kind == TiaFlgEndKind.Pin && end.Uid == part.Uid)
                .Select(end => end.Pin ?? "").Distinct().ToList();
            string[] order = part.IsCall ? [.. part.Parameters.Select(parameter => parameter.Name)]
                : InstructionPins.GetValueOrDefault(part.Name) ?? [];
            if (order.Length == 0) {
                return wired;
            }
            return [.. wired.OrderBy(pin => Array.IndexOf(order, pin) is var at and >= 0 ? at : int.MaxValue)];
        }

        private List<string> InputPins(TiaFlgPart part) =>
            [.. Pins(part).Where(pin => pin.StartsWith("in", StringComparison.Ordinal) && pin.Length > 2 && char.IsAsciiDigit(pin[2]))
                .OrderBy(pin => int.Parse(pin.AsSpan(2), System.Globalization.CultureInfo.InvariantCulture))];

        private List<string> OutputPins(TiaFlgPart part) => [.. Pins(part).Where(pin => IsOutput(part, pin))];

        private static string? Section(TiaFlgPart part, string pin) => part.Parameters.FirstOrDefault(parameter => parameter.Name == pin).Section;

        private static bool IsOutput(TiaFlgPart part, string pin) {
            if (pin is "eno" or "ENO") {
                return true;
            }
            if (part.IsCall) {
                return Section(part, pin) is "Output" or "Return";
            }
            return pin.StartsWith("out", StringComparison.Ordinal) || pin is "q" or "Q" or "ET" or "CV" or "QU" or "QD";
        }

        private static bool IsKnownGate(string name) =>
            name is "Contact" or "PContact" or "NContact" or "PBox" or "NBox" or "A" or "O" or "X" or "Not" or "Coil" or "SCoil" or "RCoil"
                or "Sr" or "Rs" or "Jump" or "JumpNot" or "Return" or "Move"
            || Comparisons.ContainsKey(name) || Arithmetic.ContainsKey(name);
    }

    private static Expr And(Expr left, Expr right) =>
        left.IsTrue ? right : right.IsTrue ? left : new Expr(left.Within(Expr.And) + " AND " + right.Within(Expr.And), Expr.And);

    private static Expr Combine(List<Expr> terms, string separator, int precedence) =>
        terms.Count == 1 ? terms[0] : new Expr(string.Join(separator, terms.Select(term => term.Within(precedence))), precedence);

    private static Expr Not(Expr value) =>
        value.IsTrue ? new Expr("FALSE", Expr.Atom) : value.Text.StartsWith("NOT ", StringComparison.Ordinal) && value.Precedence == Expr.Not
            ? new Expr(value.Text[4..], Expr.Atom) : new Expr("NOT " + value.Within(Expr.Not), Expr.Not);
}
