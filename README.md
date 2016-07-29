# Updater

Updater is a simple solution to deploy/update client applications on Windows. 

# Design Note

TBD

## AppDir Layout

```
%USERPROFILE%\app1
    updater-1\
        ...
    updater-2\
        Updater.exe
        config.json    
    tools\
        ...
    app1-0.1-dev\
    --> tools\ 
        bin\
        ...
    app1-0.1-release\
    --> tools\ 
        bin\
        ...

    app1-0.1-dev.manifest.json
    app1-0.1-release.manifest.json
```

# Updater Config

```json
{
    "appDir": "%USERPROFILE%\\app1",
    "repoUrl": "http://server:8080/pkgs/",
    "versionUrl": "app1-release.version.txt",
    "keepVersions": 2 
}
```

# App File

```
name-version-channel.json
```

```json
{
  "app": {
    "name": "app1",
    "title": "app1",
    "version": "1",
    "channel": ""
  },
  "pkgs": {
    "app1": "app1-1",
    "tool": "tool",
    "updater": "updater-1"
  },
  "layout": {
    "main": "app1",
    "deps": [
      {
        "pkg": "tool"
      }
    ]
  },
  "shortcuts": [
    {
      "name": "${app.title}-${app.version}",
      "target": "${pkgs.updater}\\updater.exe",
      "args": "${fileName}",
    }
  ],
  "launch": {
    "target": "${pkgs.app1}\\bin\\app1.exe",
    "args": "--logo"
  }
}
```
