# Playwright's own image: browsers and their system libraries are already inside, so
# nobody running these tests needs Node, npm or a browser download on their machine.
# The tag MUST track the @playwright/test version in package.json — a mismatch makes
# Playwright refuse to start ("Executable doesn't exist").
FROM mcr.microsoft.com/playwright:v1.62.1-noble

WORKDIR /e2e

# Dependencies first: this layer is rebuilt only when the lockfile changes.
COPY package.json package-lock.json ./
RUN npm ci

COPY . .

# The stack is brought up by docker compose, not by the test runner, so there is no
# webServer block to wait on — compose's healthcheck ordering does that job.
CMD ["npx", "playwright", "test"]
