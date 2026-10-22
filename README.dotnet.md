# catussh.NET

catussh 的 Windows 移植版：管 Linux / macOS 服务器的 SSH 终端、SFTP 文件和实时监控。
原项目是 macOS / iOS 应用，这里用 .NET 10 重写。Fork 自 https://github.com/mizorewww/catussh

## 现在能看什么

- Home、Servers、Identities、Snippets、Port Forward、Settings
- Monitor：CPU、内存、磁盘、网速曲线、GPU，亮色暗色都有

数据暂时是演示用的，SSH 真连接还没接。

## 运行

```bash
dotnet run --project src/Catussh.App
#   --page home|servers|monitor|identities|snippets|portforward|settings
#   --dark | --light
```

需要装 .NET 10 SDK。WebView2 运行时 Windows 10/11 一般自带。

## 许可

MIT，见 `LICENSE`。
