#Requires -Version 7.0
$ErrorActionPreference = 'Stop'

$docs = Join-Path (Split-Path -Parent $PSScriptRoot) 'docs'
$source = Join-Path $docs '用户手册.md'
$target = Join-Path $docs '用户手册.html'
$body = (ConvertFrom-Markdown -LiteralPath $source).Html

# Embed the screenshots so the installer only needs to carry one file.
$body = [regex]::Replace($body, 'src="(manual-images/[a-z-]+\.png)"', {
    param($match)
    $file = Join-Path $docs $match.Groups[1].Value
    'src="data:image/png;base64,' + [Convert]::ToBase64String([IO.File]::ReadAllBytes($file)) + '"'
})
if ([regex]::IsMatch($body, '<img\s[^>]*src="(?!data:image/png;base64,)')) {
    throw '手册截图必须从 manual-images 内嵌。'
}
$body = $body.Replace('<table>', '<div class="table-scroll" tabindex="0"><table>').Replace('</table>', '</table></div>')

$header = @'
<!doctype html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>foxmolika 用户手册</title>
<style>
:root { color-scheme: light; --ink: #192833; --muted: #415766; --link: #08676a; --rule: #d9e4e8; }
* { box-sizing: border-box; }
body { margin: 0; background: #f2f7f8; color: var(--ink); font: 1rem/1.78 "Microsoft YaHei UI", "PingFang SC", "Noto Sans CJK SC", sans-serif; }
main { max-width: 58rem; margin: 1.5rem auto; padding: clamp(1.1rem, 5vw, 3.3rem); background: #fff; border-top: .35rem solid var(--link); }
h1, h2, h3 { line-height: 1.35; text-wrap: balance; }
h1 { margin: .15rem 0 1.1rem; font-size: clamp(2.05rem, 5.6vw, 3.2rem); letter-spacing: -.025em; }
h2 { margin: 2.2rem 0 .8rem; padding-top: 1.2rem; border-top: 1px solid var(--rule); font-size: clamp(1.35rem, 4vw, 1.75rem); }
h3 { margin: 1.65rem 0 .55rem; font-size: 1.12rem; color: #204d52; }
p, ul, ol, pre { margin: .55rem 0 1rem; }
li { margin: .22rem 0; }
a { color: var(--link); text-decoration-thickness: .08em; text-underline-offset: .17em; }
a:focus-visible, .table-scroll:focus-visible { outline: 2px solid #b75d12; outline-offset: 3px; }
a[id] { scroll-margin-top: .7rem; }
img { display: block; max-width: 100%; height: auto; margin: 1.1rem 0; border: 1px solid var(--rule); border-radius: .3rem; }
code { overflow-wrap: anywhere; padding: .08em .25em; border-radius: .2em; background: #edf4f5; font: .89em/1.6 Consolas, "Courier New", monospace; }
pre { background: #142730; color: #f1f7f7; padding: 1rem; border-radius: .35rem; overflow: auto; }
pre code { background: none; padding: 0; color: inherit; font-size: .9rem; }
.table-scroll { margin: .85rem 0 1.35rem; overflow: auto; }
table { width: 100%; min-width: 32rem; border-collapse: collapse; text-align: left; }
th, td { padding: .55rem .7rem; vertical-align: top; border: 1px solid var(--rule); }
th { background: #eaf3f3; color: #234851; }
tbody tr:nth-child(even) { background: #f7fafb; }
td:first-child { width: 24%; }
@media (min-width: 43rem) {
    main > h2:first-of-type + ul { columns: 2; column-gap: 2rem; }
    main > h2:first-of-type + ul > li { break-inside: avoid; }
}
@media (max-width: 42rem) {
    main { margin: 0; }
    table { min-width: 28rem; }
}
@media print {
    body { background: #fff; font-size: 10.5pt; }
    main { margin: 0; padding: 0; max-width: none; border: 0; }
    h2, h3 { break-after: avoid; }
    pre, img, tr { break-inside: avoid; }
    pre { white-space: pre-wrap; color: #192833; background: #edf4f5; }
    img { max-height: 150mm; object-fit: contain; }
    table { min-width: 0; }
}
</style>
</head>
<body>
<main>
'@
$page = $header + "`n" + $body + "</main>`n</body>`n</html>`n"
[IO.File]::WriteAllText($target, $page.Replace("`r`n", "`n"), [Text.UTF8Encoding]::new($false))
Write-Output $target
