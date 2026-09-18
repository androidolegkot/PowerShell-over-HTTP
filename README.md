# PowerShell-over-HTTP
Lightweight PowerShell command execution server over HTTP.

**PSOVERHTTP (PowerShell over HTTP)** is a lightweight HTTP server designed to save time when giving an AI model access to PowerShell on a Windows VM.

Instead of installing gigabytes of software, dozens of dependencies, or setting up hundreds of tools, you can get a working PowerShell interface in about **5 minutes** using a single tool: **fetch-mcp**.

It is intended for AI chat clients such as:

* LM Studio
* Cherry Studio
* Chatbox
* Open WebUI

The idea is simple: **fetch-mcp gives the model access to the PSOVERHTTP HTTP API, and PSOVERHTTP executes the requested PowerShell command on the VM.**

## How it works

The server listens for HTTP requests and accepts a PowerShell command through the `/command` endpoint.

For example:

```cmd
curl "http://127.0.0.1:8080/command?timeout=3&cwd=C:\test&cmd=Write-Host%20%22Hello%20world%22"
```

PSOVERHTTP starts `powershell.exe`, executes the command, captures its output, and returns a JSON response:

```json
{
  "success": true,
  "exitCode": 0,
  "cwd": "C:\\test",
  "stdout": "Hello world\n",
  "stderr": "",
  "time": "1,326 s"
}
```

The response provides everything an AI agent needs to understand what happened:

* `success` — whether the command completed successfully
* `exitCode` — PowerShell process exit code
* `cwd` — working directory used for the command
* `stdout` — standard output
* `stderr` — standard error
* `time` — execution time

A command can also specify a timeout and working directory.

## Requirements

PSOVERHTTP is intentionally lightweight.

You only need:

* Windows
* **C# Interactive (`csi.exe`)**
* PowerShell 2.0+
* .NET Framework 4.6+
* `Newtonsoft.Json.dll`

The minimum supported Windows version is **Windows Vista SP2**.

No database, external web server, Node.js, Python, Docker, or other large framework is required.

## Running

Run with the default HTTP port:

```cmd
csi psoverhttp.csx
```

The default port is **8080**.

To use another port:

```cmd
csi psoverhttp.csx port=9000
```

To enable debug encoding logs:

```cmd
csi psoverhttp.csx port=9000 debugencode=debug.txt
```

The server can then be accessed through HTTP by curl, fetch-mcp, or any other HTTP client.

## Why PSOVERHTTP?

The goal is not to build another large agent framework.

The goal is to provide the smallest practical bridge between an AI model and a Windows VM:

**AI model → fetch-mcp → HTTP → PSOVERHTTP → PowerShell → Windows**

This makes it possible to give a model useful access to a VM with minimal setup and without installing a large collection of additional tools.

## Development

AI-assisted development: **ChatGPT** (chatgpt.com)
