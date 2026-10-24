@echo off
rem =====================================================================
rem  ChokePoint - build the installer with one double-click.
rem  Keep this file next to build.bat, in:  .\ChokePointTeams\
rem  Needs Inno Setup 6 (free) installed. The result is:
rem     .\ChokePointTeams\Builds\ChokePointforTeams-Setup-<date>.exe
rem =====================================================================
call "%~dp0build.bat" installer
