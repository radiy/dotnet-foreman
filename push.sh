rm -r output
dotnet.exe pack -c Release -o output
dotnet.exe nuget push -k $1 output/*.nupkg -s https://api.nuget.org/v3/index.json
