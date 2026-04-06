FROM mcr.microsoft.com/dotnet/runtime-deps:10.0

RUN groupadd -r vms && useradd -r -g vms -s /sbin/nologin vms

COPY server/ /app/

RUN mkdir /data && chown vms:vms /data

USER vms
WORKDIR /app

EXPOSE 4433 8080

ENTRYPOINT ["/app/Server", "--data-path", "/data", "--bind", "0.0.0.0"]
