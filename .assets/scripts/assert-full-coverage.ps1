<#
.SYNOPSIS
Requires full reported line, branch and executable-method coverage for core and adapter assemblies.
.PARAMETER CoverageDirectory
Fresh Coverlet results directory containing both assembly-specific test runs for one configuration.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$CoverageDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-Count([System.Xml.XmlElement]$Element, [string]$Name) {
    [long]$value = 0
    if (-not [long]::TryParse($Element.GetAttribute($Name), [ref]$value) -or $value -lt 0) {
        throw "Missing or invalid coverage count '$Name'."
    }
    return $value
}

$expectedAssemblies = @('Trailblazer', 'Trailblazer.Gravitas')
$foundAssemblies = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$reports = @(Get-ChildItem -LiteralPath $CoverageDirectory -Filter coverage.cobertura.xml -File -Recurse | Sort-Object FullName)
if ($reports.Count -eq 0) { throw 'No Cobertura coverage reports found.' }

foreach ($report in $reports) {
    [xml]$document = Get-Content -LiteralPath $report.FullName -Raw
    $coverage = $document.DocumentElement
    if ($coverage.Name -ne 'coverage') { throw "Invalid Cobertura report: $($report.FullName)" }
    $packages = @($coverage.SelectNodes('packages/package'))
    if ($packages.Count -eq 0) { throw "No coverage packages in $($report.FullName)." }

    $lines = Read-Count $coverage 'lines-valid'
    $coveredLines = Read-Count $coverage 'lines-covered'
    $branches = Read-Count $coverage 'branches-valid'
    $coveredBranches = Read-Count $coverage 'branches-covered'
    if ($lines -le 0 -or $branches -le 0 -or $coveredLines -ne $lines -or $coveredBranches -ne $branches) {
        throw "Coverage gate failed in $($report.FullName): lines $coveredLines/$lines; branches $coveredBranches/$branches."
    }

    foreach ($package in $packages) {
        $name = $package.GetAttribute('name')
        if ($name -cnotin $expectedAssemblies) { throw "Unexpected coverage package '$name'." }
        if (@($packages | Where-Object { $_.GetAttribute('name') -ceq $name }).Count -ne 1) {
            throw "Duplicate coverage package '$name' in $($report.FullName)."
        }
        $methods = @($package.SelectNodes('classes/class/methods/method[lines/line]'))
        if ($methods.Count -eq 0) { throw "No executable methods reported for $name." }

        foreach ($line in $package.SelectNodes('classes/class/lines/line | classes/class/methods/method/lines/line')) {
            if ((Read-Count $line 'hits') -eq 0) {
                throw "Uncovered line in ${name}: $($line.GetAttribute('number'))."
            }
            if ($line.GetAttribute('branch') -eq 'true' -and
                $line.GetAttribute('condition-coverage') -notmatch '^100% \(([1-9][0-9]*)/\1\)$') {
                throw "Uncovered or invalid branch in ${name}: $($line.GetAttribute('number'))."
            }
        }
        if (-not $foundAssemblies.Add($name)) { throw "Duplicate coverage report for $name." }
        Write-Output "${name}: full reported line/branch coverage; $($methods.Count) executable methods covered."
    }
}

foreach ($name in $expectedAssemblies) {
    if (-not $foundAssemblies.Contains($name)) { throw "Missing coverage for $name." }
}
