# Playwright's own image: browsers and their system libraries are already inside, so
# nobody running these tests needs Node, npm or a browser download on their machine.
# The tag MUST track the @playwright/test version in package.json — a mismatch makes
# Playwright refuse to start ("Executable doesn't exist").
FROM mcr.microsoft.com/playwright:v1.62.1-noble

WORKDIR /e2e

# Dependencies first: this layer is rebuilt only when the lockfile changes.
COPY package.json package-lock.json ./
RUN npm ci

# Named explicitly instead of `COPY . .`: the build context also holds node_modules and the
# generated report folders, and a recursive copy is one stray file away from baking something
# into the image that was never meant to be there. .dockerignore covers today's list; naming
# the sources covers tomorrow's.
COPY playwright.config.ts tsconfig.json ./
COPY tests ./tests

# The image ships a non-root account (pwuser, uid 1000) for exactly this. /e2e is handed over
# with it so the run can write its report and traces — compose bind-mounts those two folders.
RUN chown -R pwuser:pwuser /e2e
USER pwuser

# The stack is brought up by docker compose, not by the test runner, so there is no
# webServer block to wait on — compose's healthcheck ordering does that job.
CMD ["npx", "playwright", "test"]
