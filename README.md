# catussh.NET

管 Linux / macOS 服务器的 SSH 终端、文件传输和实时监控。
原项目是 macOS / iOS 应用，这里用 .NET 10 重写。Fork 自 https://github.com/mizorewww/catussh

## 现在能看什么

- Home、Servers、Identities、Snippets、Port Forward、Settings
- Monitor：CPU、内存、磁盘、网速曲线、GPU，亮色暗色都有

能用的：Quick Connect、新建服务器、新建身份（密码或密钥）、真实 SSH 连接与监控。
服务器列表和身份存在 `%APPDATA%\catussh.NET\store.json`，密码用 Windows 账户加密。

还没做的：Terminal 页、FileTransfer 页、Snippets 和 Port Forward 的实际执行。

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
