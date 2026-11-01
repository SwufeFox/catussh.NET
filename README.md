# catussh.NET

管 Linux / macOS 服务器的 SSH 终端、文件传输和实时监控。
原项目是 macOS / iOS 应用，这里用 .NET 10 重写。Fork 自 https://github.com/mizorewww/catussh

## 现在能用什么

- Home、Servers、Identities、Snippets、Port Forward、Settings
- Monitor：CPU、内存、磁盘、网速曲线和 GPU，支持亮色与暗色
- Terminal：可交互 SSH shell，支持普通 SSH、Cloudflare Access 和 UU Remote
- Files：SFTP 浏览目录、创建文件夹、上传和下载；可配置每台服务器的起始目录
- Remote Desktop：VNC 键鼠控制、SSH 转发，以及 UU Remote WebRTC 桌面
- Servers 页面可通过 mDNS 发现附近的 VNC / Screen Sharing 主机，并预填服务器配置

Quick Connect、新建服务器与身份、真实 SSH 连接和服务器监控均可用。服务器和身份保存在 `%APPDATA%\catussh.NET\store.json`，密码由 Windows 账户加密。

Snippets 和 Port Forward 页面目前没有实际执行功能。

## 运行

```bash
dotnet run --project src/Catussh.App
#   --page home|servers|monitor|identities|snippets|portforward|settings
#   --dark | --light
```

需要装 .NET 10 SDK。WebView2 运行时 Windows 10/11 一般自带。

## 官网

`site/` 是产品页，用同一套设计系统：

```bash
node tools/sync-site.mjs                      # 同步设计文件
node tools/serve.mjs 5201 site                # 本地预览
```

部署由 Cloudflare Pages 的 Git 集成负责，push 到 main 自动构建：

| 设置 | 值 |
| --- | --- |
| Build command | `node tools/sync-site.mjs` |
| Build output directory | `site` |
| Root directory | 留空 |

构建时会重新同步设计文件，所以官网永远和应用当前的样子一致。
站点绑定 `catssh.swufefox.com`（`site/_redirects` 把 `www` 301 过去）。

## 许可

MIT，见 `LICENSE`。
