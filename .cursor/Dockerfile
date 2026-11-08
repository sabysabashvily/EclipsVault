# Cloud Agent base image for EclipsVault.
# Paths in environment.json are relative to .cursor/; context is the repo root.
FROM ubuntu:24.04

ARG NODE_VERSION=24.22.0
ARG DOTNET_CHANNEL=10.0
ARG DOCKER_CE_VERSION=5:28.5.2-1~ubuntu.24.04~noble

ENV DEBIAN_FRONTEND=noninteractive
ENV DOTNET_ROOT=/usr/local/share/dotnet
ENV PATH=/usr/local/share/dotnet:/usr/local/bin:${PATH}

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        git \
        gnupg \
        xz-utils \
        build-essential \
        python3 \
        iptables \
        fuse-overlayfs \
        fuse3 \
    && rm -rf /var/lib/apt/lists/*

# Node from the version pinned in EclipsVault.AngularUi/.nvmrc
RUN curl -fsSL "https://nodejs.org/dist/v${NODE_VERSION}/node-v${NODE_VERSION}-linux-x64.tar.xz" \
       | tar -xJ -C /usr/local --strip-components=1 --no-same-owner

# .NET 10 SDK
RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && bash /tmp/dotnet-install.sh --channel "${DOTNET_CHANNEL}" --install-dir /usr/local/share/dotnet \
    && ln -sfn /usr/local/share/dotnet/dotnet /usr/local/bin/dotnet \
    && rm /tmp/dotnet-install.sh

# Docker Engine for nested containers (integration + e2e Testcontainers)
RUN install -m 0755 -d /etc/apt/keyrings \
    && curl --retry 3 --retry-delay 5 -fsSL https://download.docker.com/linux/ubuntu/gpg \
       | gpg --dearmor -o /etc/apt/keyrings/docker.gpg \
    && chmod a+r /etc/apt/keyrings/docker.gpg \
    && . /etc/os-release \
    && echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu ${VERSION_CODENAME} stable" \
       > /etc/apt/sources.list.d/docker.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends \
        docker-ce=${DOCKER_CE_VERSION} \
        docker-ce-cli=${DOCKER_CE_VERSION} \
        containerd.io \
        docker-buildx-plugin \
        docker-compose-plugin \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /etc/docker \
    && printf '%s\n' '{' '  "storage-driver": "fuse-overlayfs"' '}' > /etc/docker/daemon.json \
    && update-alternatives --set iptables /usr/sbin/iptables-legacy \
    && update-alternatives --set ip6tables /usr/sbin/ip6tables-legacy \
    && usermod -aG docker ubuntu

USER ubuntu
WORKDIR /home/ubuntu
