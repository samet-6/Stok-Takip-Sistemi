# E2E testleri (Playwright)

Uçtan uca testler tüm yığını sürer (postgres + api + web), bu yüzden `web/`'in içinde değil
kökte ayrı bir proje olarak durur.

## Çalıştırma — Docker (önerilen, hiçbir kurulum gerektirmez)

Depo kökünde:

```bash
docker compose --profile e2e up --build --abort-on-container-exit --exit-code-from e2e
```

Testler `web` konteynerine karşı koşar; çıkış kodu testlerin sonucudur. Rapor
`e2e/playwright-report/`, başarısızlık kanıtları `e2e/test-results/` altına yazılır.

## Çalıştırma — yerel (geliştirme)

Node 20+ gerekir. Bir kez:

```bash
cd e2e
npm ci
npx playwright install chromium
```

Sonra, hedefe göre:

```bash
# Docker yığını ayakta (docker compose up -d --build)
npm test

# Vite dev sunucusu + dotnet API ayakta
E2E_BASE_URL=http://127.0.0.1:5173 npm test     # PowerShell: $env:E2E_BASE_URL='http://127.0.0.1:5173'; npm test
```

## Durum

`tests/` klasörü şu an **boş**: bu oturumda yalnız ortam kuruldu, test yazılmadı.
Test dosyası eklenene kadar `npm test` *"no tests found"* deyip 1 ile çıkar — beklenen davranış.

Sürüm notu: `Dockerfile`'daki imaj etiketi ile `package.json`'daki `@playwright/test`
sürümü **aynı olmak zorunda** (bugün `1.62.1`).
