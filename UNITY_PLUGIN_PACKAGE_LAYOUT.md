# Unity Plugins 打包布局

最终下载产物为 `RustAV-UnityPlugins.zip`，根目录结构如下：

```text
UnityPlugins/
  Assets/
    Plugins/
      x86_64/
        rustav_native.dll
        *.dll
        DEPENDENCIES.txt
      Android/
        arm64-v8a/
          librustav_native.so
          DEPENDENCIES.txt
      iOS/
        librustav_native.a
        RustAV.h
        DEPENDENCIES.txt
  BuildSupport/
    iOS/
      RustAV.xcframework
```

布局约定：

1. `Assets/Plugins/x86_64`：
   Windows 原生插件目录，放 `rustav_native.dll` 及其运行时依赖 DLL。
2. `Assets/Plugins/Android/arm64-v8a`：
   Android `arm64-v8a` 原生插件目录，放 `librustav_native.so`。
3. `Assets/Plugins/iOS`：
   iOS 原生插件目录，放 `librustav_native.a` 和 `RustAV.h`，可直接随 Unity 导出到 Xcode。
4. `BuildSupport/iOS/RustAV.xcframework`：
   额外提供的 iOS 构建支持产物，不直接放到 `Assets` 下，避免 Unity 自动导入时混入 simulator 变体。

调用约定：

1. Windows / Android：`DllImport("rustav_native")`
2. iOS：`DllImport("__Internal")`
