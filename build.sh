#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
dotnet test -c Release --filter "Category!=Perf"
rm -rf publish AgentFileInspector.zip
dotnet publish src/OdcAgentFileInspector/OdcAgentFileInspector.csproj -c Release --no-self-contained -o publish
(cd publish && zip -r ../AgentFileInspector.zip .)
echo "Built $(pwd)/AgentFileInspector.zip"
