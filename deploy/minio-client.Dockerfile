FROM alpine:3.23.7

RUN addgroup -S guardacat \
    && adduser -S -G guardacat -h /home/guardacat guardacat \
    && apk add --no-cache minio-client=0.20250813.083541-r7

USER guardacat
