function show(level) {
  document.getElementById("level").style.height = Math.max(0, Math.min(100, level)) + "%";
}
show(40);
