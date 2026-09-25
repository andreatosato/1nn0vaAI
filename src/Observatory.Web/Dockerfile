FROM node:24-alpine AS build
WORKDIR /app
COPY src/Observatory.Web/package.json src/Observatory.Web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY src/Observatory.Web/index.html src/Observatory.Web/tsconfig.json src/Observatory.Web/vite.config.ts src/Observatory.Web/vitest.config.ts ./
COPY src/Observatory.Web/src ./src
COPY src/Observatory.Web/server ./server
COPY src/Observatory.Web/public ./public
RUN npm run build

FROM nginx:alpine AS runtime
ENV NGINX_ENVSUBST_FILTER="^(INLINE_API_URL|SKILLS_API_URL|A2A_API_URL)$"
COPY --chmod=755 src/Observatory.Web/nginx/15-api-origins.envsh /docker-entrypoint.d/15-api-origins.envsh
COPY src/Observatory.Web/nginx/default.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 CMD wget -q -O /dev/null http://127.0.0.1:8080/health || exit 1
