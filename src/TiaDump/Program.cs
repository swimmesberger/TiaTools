using ConsoleAppFramework;
using TiaDump;

var app = ConsoleApp.Create();
app.Add<TiaDumpCommands>();
app.Run(args);
