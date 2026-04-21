' ═══════════════════════════════════════════════════════════════
'  히트판 ERP 시작 스크립트 (CMD 창 노출 없이 실행)
'  - HitPan.API.exe 백그라운드 실행
'  - 3초 후 기본 브라우저로 http://localhost:5234 열기
' ═══════════════════════════════════════════════════════════════
Option Explicit

Dim oShell, oFSO, sScriptDir, sExePath

Set oShell = CreateObject("WScript.Shell")
Set oFSO = CreateObject("Scripting.FileSystemObject")

' 스크립트 위치 = 설치 폴더
sScriptDir = oFSO.GetParentFolderName(WScript.ScriptFullName)
sExePath = sScriptDir & "\hitpan\HitPan.API.exe"

If Not oFSO.FileExists(sExePath) Then
    MsgBox "히트판 실행파일을 찾을 수 없습니다:" & vbCrLf & sExePath, vbCritical, "히트판 ERP"
    WScript.Quit 1
End If

' 이미 실행 중인지 확인 (포트 5234 또는 5257)
Dim oExec, sOut
Set oExec = oShell.Exec("cmd /c netstat -an | findstr "":5234""")
oExec.StdIn.Close
sOut = oExec.StdOut.ReadAll
If InStr(sOut, "LISTENING") > 0 Then
    ' 이미 실행 중 → 브라우저만 열기
    oShell.Run "http://localhost:5234", 1, False
    WScript.Quit 0
End If

' API 실행 (완전 백그라운드, 창 없음)
' 작업 디렉토리를 exe 위치로 설정해야 wwwroot·appsettings 경로 정상
Dim sWorkDir
sWorkDir = sScriptDir & "\hitpan"
oShell.CurrentDirectory = sWorkDir
oShell.Run """" & sExePath & """", 0, False

' 서버가 리스닝 시작할 때까지 최대 15초 대기
Dim i
For i = 1 To 15
    WScript.Sleep 1000
    Set oExec = oShell.Exec("cmd /c netstat -an | findstr "":5234"" | findstr LISTENING")
    oExec.StdIn.Close
    sOut = oExec.StdOut.ReadAll
    If InStr(sOut, "LISTENING") > 0 Then
        Exit For
    End If
Next

' 기본 브라우저로 히트판 웹 열기
oShell.Run "http://localhost:5234", 1, False

Set oShell = Nothing
Set oFSO = Nothing
