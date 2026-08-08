# NanaW上号器

官网：shop.nw-s.lat

全网最小 Steam 卡密上号器。服务器已内置，开箱即用。

## 使用

1. 运行 `NanaW.exe`
2. 输入卡密，点击「登录 Steam」
3. 自动写入登录信息并启动 Steam

## 构建

需要 .NET 8 SDK + Windows 10+。

```bash
dotnet publish NanaW.csproj -c Release -f net8.0-windows --self-contained false
```

输出在 `bin/Release/net8.0-windows/win-x64/publish/`

## 文件

| 文件 | 说明 |
|------|------|
| `Program.cs` | 主程序 |
| `NanaW.csproj` | 项目配置 |
| `icon.ico` | 图标 |
| `server.ini` | 可选，覆盖内置服务器 |

## 许可证

MIT
