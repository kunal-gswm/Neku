<#
.SYNOPSIS
    Send a developer event to Neku desktop companion.
.EXAMPLE
    .\neku-event.ps1 build_success "Build passed in 2.1s!"
    .\neku-event.ps1 test_fail "3 tests failed in Core.Tests"
#>
param(
    [Parameter(Position=0, Mandatory=$true)]
    [string]$Type,

    [Parameter(Position=1)]
    [string]$Message = "",

    [Parameter()]
    [int]$Port = 48123
)

$payload = @{
    type = $Type
    message = $Message
} | ConvertTo-Json

try {
    $response = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/event" -Method Post -Body $payload -ContentType "application/json" -TimeoutSec 2 -ErrorAction Stop
} catch {
    # If Neku is not running or developer events are disabled, silently exit without failing the caller's build
}
