# The names and messages of failed tests as annotations of the run, where they can be read
# without the log.
param([string] $folder)

if (-not (Test-Path $folder)) { return }
foreach ($file in Get-ChildItem $folder -Filter *.trx -Recurse) {
    $results = ([xml](Get-Content $file.FullName -Raw)).TestRun.Results.UnitTestResult
    foreach ($test in $results | Where-Object outcome -eq "Failed") {
        $message = "$($test.Output.ErrorInfo.Message)" -replace "\s+", " "
        $where = ("$($test.Output.ErrorInfo.StackTrace)" -split "`n" | Where-Object { $_ -match "\.cs:line" } | Select-Object -First 1) -replace "^.*[\\/]", ""
        Write-Output "::error title=$($test.testName)::$message $where"
    }
}
