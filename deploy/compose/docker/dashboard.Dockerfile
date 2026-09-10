# Comuki dashboard image — the Vite SPA served by nginx, with nginx
# also reverse-proxying the API to the host container. One origin in
# the browser: no CORS preflight, cookies just work.
#
# Build context = repo root:
#   docker build -f deploy/compose/docker/dashboard.Dockerfile \
#     --build-arg VITE_API_BASE_URL=http://localhost:17173 \
#     -t comuki-dashboard:local .
#
# VITE_API_BASE_URL is BAKED at build time (Vite define). Point it at
# the public URL users open in the browser.

# ---------- Stage 1: build the SPA ----------
FROM oven/bun:1.4.0-slim AS build

ARG VITE_API_BASE_URL=http://localhost:17173
ARG VITE_REPO_URL=https://github.com/dot-stbl/comuki

WORKDIR /src
COPY dashboard/package.json dashboard/.prettierrc dashboard/.prettierignore ./
COPY dashboard/tsconfig.json dashboard/tsconfig.app.json dashboard/tsconfig.node.json ./
COPY dashboard/vite.config.ts dashboard/index.html dashboard/vitest.config.ts dashboard/vitest.setup.ts ./
COPY dashboard/eslint.config.js dashboard/kubb.config.ts ./
COPY dashboard/src ./src

# Real backend mode. The generated kubb client tree is committed, so a
# plain install + build works from a clean clone.
ENV VITE_USE_MOCK=false \
    VITE_API_BASE_URL=${VITE_API_BASE_URL} \
    VITE_REPO_URL=${VITE_REPO_URL}

RUN bun install \
    && bun run build

# ---------- Stage 2: nginx ----------
FROM nginx:1.27-alpine

COPY deploy/compose/docker/dashboard.nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/dist /usr/share/nginx/html

EXPOSE 80
