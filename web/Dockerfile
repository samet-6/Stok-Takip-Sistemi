# Node 24 "Krypton" — Active LTS as of 2025-10, not EOL.
FROM node:24-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

# The unprivileged variant rather than `nginx:alpine` plus a USER line: the stock image's
# entrypoint needs root to write /var/cache/nginx and the pid file, so adding USER to it
# yields a container that starts and then fails. This image is built for the job — master
# and workers all run as uid 101.
#
# It listens on 8080, and that is kept rather than moved back to 80. Not because 80 is
# impossible: measured in this stack, a non-root nginx binds it fine, since Docker sets
# net.ipv4.ip_unprivileged_port_start=0 by default. That default is the point — rootless
# daemons and hardened runtimes put it back to 1024, and an image that works only under a
# permissive sysctl is a surprise waiting for the next host. 8080 needs no such permission.
# This port is why nginx.conf, the compose port mapping, the healthcheck and the e2e base
# URL all say 8080.
FROM nginxinc/nginx-unprivileged:alpine AS final
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 8080
