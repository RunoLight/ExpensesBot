@echo off
cd /d "%~dp0"
echo Running dotnet test... (output goes to test-output.txt)
dotnet --version > test-output.txt 2>&1
dotnet test ExpensesBot.slnx --nologo -v q >> test-output.txt 2>&1
echo EXIT CODE: %ERRORLEVEL% >> test-output.txt
echo Done. You can close this window.
