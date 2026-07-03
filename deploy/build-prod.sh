#!/usr/bin/env bash
# 生产打包(开发机运行):一条命令出 compose + 镜像,再 docker save。
#
#   1. aspire do prepare-prod  → 生成 docker-compose.yaml + .env.Production + build 镜像
#      (各项目经 Program.cs 里的 PublishAsDockerFile(WithDockerfile) 用自带 Dockerfile build;
#       容器级配置由 ConfigureComposeFile 写进 compose)
#   2. prepare 给镜像打的 tag 是 <资源>:<sha>,但 .env.Production 引用的是时间戳 tag(对不上,
#      那个 tag 只有 aspire deploy 才会上到镜像;无 registry 时也没有任何 API 能改 build tag)。
#      这里读实际 build 出的 sha,重写 .env 对齐。
#   3. docker save 打包这两个镜像
#
# 产物:deploy/output/dist/{docker-compose.yaml, .env, images.tar}
# 之后:scp dist/* 到服务器,在服务器跑 deploy/output/deploy.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DIST="$ROOT/deploy/output/dist"

echo "==> [1/3] aspire do prepare-prod(生成 compose + build 镜像)"
rm -rf "$DIST" "$ROOT/demo/AppHost/aspire-output"
aspire do prepare-prod --project "$ROOT/demo/AppHost" -o "$DIST" --non-interactive

# prepare 产出 .env(空占位)+ .env.Production(填了时间戳 tag)。并成单一 .env。
if [ -f "$DIST/.env.Production" ]; then
  cp "$DIST/.env.Production" "$DIST/.env"
  rm -f "$DIST/.env.Production"
fi

echo "==> [2/3] 读实际 build 出的镜像(<资源>:<sha>),重写 .env 对齐"
# prepare-prod 的 .env 引用时间戳 tag(不存在);真实 tag 是 <资源>:<sha>。按资源名取最新 build 的那个。
FEISHU_IMG=$(docker images feishu --format '{{.Repository}}:{{.Tag}}' | head -n1)
DASH_IMG=$(docker images dashboard --format '{{.Repository}}:{{.Tag}}' | head -n1)
if [ -z "$FEISHU_IMG" ] || [ -z "$DASH_IMG" ]; then
  echo "❌ 没找到 build 出的镜像(feishu='$FEISHU_IMG' dashboard='$DASH_IMG'),prepare-prod 真的 build 了吗?" >&2
  exit 1
fi
sed -i "s|^FEISHU_IMAGE=.*|FEISHU_IMAGE=$FEISHU_IMG|"     "$DIST/.env"
sed -i "s|^DASHBOARD_IMAGE=.*|DASHBOARD_IMAGE=$DASH_IMG|" "$DIST/.env"
echo "  FEISHU_IMAGE=$FEISHU_IMG"
echo "  DASHBOARD_IMAGE=$DASH_IMG"

echo "==> [3/3] docker save → images.tar"
docker save -o "$DIST/images.tar" "$FEISHU_IMG" "$DASH_IMG"

echo
echo "✅ 产物:"; ls -lah "$DIST"
echo
echo "下一步——传到服务器并部署:"
echo "  scp $DIST/docker-compose.yaml $DIST/.env $DIST/images.tar <server>:~/mib/"
echo "  # 服务器侧(把 deploy/output/deploy.sh 也拷到 ~/mib/):"
echo "  ssh <server> 'cd ~/mib && podman load -i images.tar && podman compose -f docker-compose.yaml up -d'"
