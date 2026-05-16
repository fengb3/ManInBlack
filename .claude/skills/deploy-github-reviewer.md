---
name: deploy-github-reviewer
description: 部署 GitHub Code Reviewer 到阿里云服务器（构建镜像、上传、重启容器）
triggers:
  - deploy
  - 部署
  - github reviewer
  - code reviewer
---

# 部署 GitHub Code Reviewer

将 GitHub Code Reviewer 部署到阿里云服务器。

## 前置条件

- 本地已安装 Docker
- SSH 别名 `aliyun` 已配置（指向 101.201.30.166）
- 远程服务器已安装 podman
- `~/.man-in-black/settings.json` 已在远程服务器配置好（含 GitHub App 配置和 AI Provider）

## 部署步骤

### 1. 构建 Docker 镜像

```bash
docker build -f demo/GitHubAdaptor/Dockerfile -t github-adaptor .
```

### 2. 导出并上传镜像

```bash
docker save github-adaptor | gzip > /tmp/github-adaptor.tar.gz
scp /tmp/github-adaptor.tar.gz aliyun:/tmp/github-adaptor.tar.gz
```

### 3. 远程加载并重启容器

```bash
ssh aliyun "podman load < /tmp/github-adaptor.tar.gz"
ssh aliyun "podman rm -f github-adaptor"
ssh aliyun "podman run -d --name github-adaptor -p 11888:8080 -v /root/.man-in-black:/root/.man-in-black github-adaptor"
```

### 4. 验证

```bash
ssh aliyun "podman logs --tail 5 github-adaptor"
```

应看到 `Now listening on: http://[::]:8080`。

健康检查：`curl http://101.201.30.166:11888/health` 应返回 `{"status":"healthy"}`。

## 配置

容器挂载 `/root/.man-in-black` 目录，`settings.json` 结构：

```json
{
  "Providers": { ... },
  "ModelChoices": { ... },
  "Agents": {
    "github-reviewer": {
      "Instruction": "你是一个代码审查专家...",
      "PipelineName": "github"
    }
  },
  "GitHub": {
    "AppId": 123456,
    "PrivateKeyPath": "/root/.man-in-black/github-app.pem",
    "WebhookSecret": "whsec_xxx",
    "WebhookEndpoint": "/github/webhook"
  }
}
```

## 故障排查

- 查看日志：`ssh aliyun "podman logs --tail 50 github-adaptor"`
- 进入容器：`ssh aliyun "podman exec -it github-adaptor bash"`
- 重启容器：`ssh aliyun "podman restart github-adaptor"`
- Webhook 签名验证失败：检查 `GitHub:WebhookSecret` 是否与 GitHub App 设置一致
- Agent 执行失败：检查 Provider 配置和 API Key 是否有效
