# GWYF Community Network API — plain Node runtime, SQLite on a persistent volume.
# Build happens inside the image: pnpm install (frozen lockfile) then pnpm build
# (tsc -p for packages/db, packages/protocol, apps/api). Runtime is plain Node —
# `node apps/api/dist/server.js` — the same artifact pnpm build:smoke guards.
FROM node:24-slim

ENV PNPM_HOME=/pnpm
ENV PATH="${PNPM_HOME}:${PATH}"

WORKDIR /app

# better-sqlite3 normally loads a prebuilt binary (linux-x64 glibc); the build
# tools are a fallback so pnpm install cannot fail on a platform mismatch.
RUN apt-get update \
    && apt-get install -y --no-install-recommends python3 make g++ \
    && rm -rf /var/lib/apt/lists/* \
    && corepack enable

COPY . .

RUN pnpm install --frozen-lockfile \
    && pnpm build

ENV NODE_ENV=production
ENV HOST=0.0.0.0
EXPOSE 3000

CMD ["node", "apps/api/dist/server.js"]