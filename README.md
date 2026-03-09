# RustAV

RustAV 公开仓负责 Unity 插件包装、示例工程、CI 编排和 Release 发布。
核心 Rust 源码已经拆分到私有仓 `zero16832/RustAV-Core`。

## 仓库职责

- 当前仓库：Unity 示例、SDK 头文件、构建脚本、QA 脚本、GitHub Actions、Release 资产
- 私有仓 `RustAV-Core`：Rust 核心源码、examples、tests、第三方补丁代码、内部设计文档

## 本地开发

建议将两个仓库放在同级目录：

```powershell
git clone https://github.com/zero16832/RustAV.git
git clone https://github.com/zero16832/RustAV-Core.git
python .\scripts\ci\build_unity_plugins.py --public-root . --core-root ..\RustAV-Core --platform windows
```

## GitHub Actions 凭据

优先方案是 GitHub App，兼容回退到 PAT。

公开仓 `RustAV` 需要：

- `vars.RUSTAV_CORE_APP_ID`
- `secrets.RUSTAV_CORE_APP_PRIVATE_KEY`
- 或 `secrets.RUSTAV_CORE_CLONE_TOKEN`
- `secrets.UNITY_LICENSE`
- `secrets.UNITY_EMAIL`
- `secrets.UNITY_PASSWORD`

私有仓 `RustAV-Core` 需要：

- `vars.RUSTAV_PUBLIC_APP_ID`
- `secrets.RUSTAV_PUBLIC_APP_PRIVATE_KEY`
- 或 `secrets.RUSTAV_PUBLIC_REPO_DISPATCH_TOKEN`

## 触发关系

1. `RustAV-Core` push 到 `main/master` 后先完成 core CI。
2. core CI 通过后向公开仓发送 `repository_dispatch`。
3. `RustAV` Release workflow checkout 对应 `core_sha`，构建 Unity 插件包和示例程序并发布 Release。
