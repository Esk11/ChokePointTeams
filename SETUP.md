# ChokePoint Teams - setup

Layout (same as ChokePoint):
  D:\GitHub\ChokePointTeams\
    build.bat  build_as_installer.bat  ChokePointTeams.iss
    ChokePointTeams\          <- the project (this zip)

Copy these from your ChokePoint project folder into ChokePointTeams\ChokePointTeams\ :
  app.manifest  choke-point-glyph.ico  WinDivert.dll  WinDivert64.sys
  THIRD-PARTY-NOTICES.txt  licenses\  assets\  ui\fonts\

Then run build.bat (or build_as_installer.bat).
