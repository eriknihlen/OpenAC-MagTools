# Live results

One row per feature, verified against a running client and a live ACE server.
Automated tests say the code does what it was written to do; these rows say the
feature works in the game.

Protocol: one live session at a time (ACE allows one session per account).
Launch the client with the plugin deployed (`tools/deploy.ps1`), exercise the
steps, record what was observed, and give a verdict of PASS, FAIL or PARTIAL.
A FAIL or PARTIAL row names the issue it is tracked under.

| Feature | Date | Build | Steps | Observed | Verdict |
|---|---|---|---|---|---|
