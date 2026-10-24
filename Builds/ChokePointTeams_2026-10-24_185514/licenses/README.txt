Files this folder must contain before you ship ChokePoint
==========================================================

Already here:
  WinDivert-LICENSE.txt           the WinDivert license (copied from its source package)

You need to add (copy the files; do not rename them):
  WinDivert-2.2.2-Source.zip      the WinDivert-2.2.2-Source.zip you downloaded.
                                  Shipping it satisfies the LGPL requirement to make the
                                  library's source code available.
  Bangers-OFL.txt                 the OFL.txt that comes in the Bangers font download
                                  (rename it to Bangers-OFL.txt).
  TraceEvent-LICENSE.txt          the license file inside the TraceEvent NuGet package.
  WebView2-LICENSE.txt            the license file inside the Microsoft.Web.WebView2 NuGet package.
  DotNet-LICENSE.txt              the MIT license of .NET: https://github.com/dotnet/runtime/blob/main/LICENSE.TXT

To find the two NuGet license files on your PC, run this in PowerShell:

  Get-ChildItem -Recurse "$env:USERPROFILE\.nuget\packages\microsoft.diagnostics.tracing.traceevent","$env:USERPROFILE\.nuget\packages\microsoft.web.webview2" -Include LICENSE*,*license*.txt,*license*.md | Select-Object FullName
