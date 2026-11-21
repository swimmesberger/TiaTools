if (Tags("Level").Read() > Tags("Target").Read()) {
  return 0xFFFF0000;
}
return 0xFF00FF00;
