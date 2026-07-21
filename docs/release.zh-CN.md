# 双端发布

推送 `v*` 标签后，GitHub Actions 会执行一次还原、测试和 Windows x64 自包含构建，然后把完全相同的 ZIP、EXE 与 SHA-256 校验文件发布到 GitHub 和 Gitee。

仓库需要配置一个 GitHub Actions Secret：

```text
GITEE_TOKEN
```

该值是在 Gitee 的“私人令牌”页面创建的访问令牌，需要仓库读写权限。工作流使用它同步 `main` 与当前标签、创建 Gitee Release 并上传附件。Gitee 发布成功后才会创建 GitHub Release；缺少 Secret 时任务会在发布附件前明确失败。

Gitee 仓库附件总容量有限，因此工作流只保留最近四个正式版本的二进制附件；旧版本的标签、Release 说明和源码归档仍然保留。应用默认优先从 Gitee 检查和下载更新，失败时自动回退 GitHub。
