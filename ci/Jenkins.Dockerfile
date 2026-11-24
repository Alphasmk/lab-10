FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet
FROM jenkins/jenkins:lts-jdk21
USER root
RUN apt-get update && apt-get install -y --no-install-recommends docker.io curl ca-certificates && rm -rf /var/lib/apt/lists/*
COPY --from=dotnet /usr/share/dotnet /usr/share/dotnet
RUN ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet && \
    curl -fsSL -o /usr/local/bin/kubectl https://dl.k8s.io/release/v1.36.1/bin/linux/amd64/kubectl && chmod +x /usr/local/bin/kubectl
RUN JENKINS_UC_DOWNLOAD=https://archives.jenkins.io jenkins-plugin-cli --plugins git workflow-job workflow-cps pipeline-model-definition
ENV DOTNET_ROOT=/usr/share/dotnet
ENV KUBECONFIG=/var/jenkins_home/kube/config
