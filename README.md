# Hidden Star

Günlük futbolcu-sezon tahmin oyunu. React arayüzü ile .NET API üretimde aynı container içinde ve aynı origin üzerinden çalışır.

## Yerel geliştirme

API:

    dotnet run --project HiddenSeason.Api

Web:

    cd web
    npm ci
    npm run dev

Vite, API isteklerini launchSettings içindeki HTTP portu 5003'e yönlendirir.

## Günlük oyuncu kataloğu

tools/generate_catalog.py, dcaribou/transfermarkt-datasets kaynağındaki players, appearances, games, clubs ve competitions CSV dosyalarını birleştirir. Havuz İngiltere, İspanya, İtalya, Almanya ve Fransa liglerinin tamamını; Türkiye'de Fenerbahçe, Galatasaray, Beşiktaş ve Trabzonspor'u; Portekiz'de Porto, Benfica ve Sporting'i; Hollanda'da Ajax, PSV ve Feyenoord'u kapsar. Oyuncu ayrıca en az 18 maç ve 900 dakika oynamalıdır. Büyük beş lig için 20 milyon avro güncel veya 30 milyon avro kariyer zirvesi; seçilmiş dev kulüpler için 10 milyon avro güncel veya 20 milyon avro kariyer zirvesi aranır.

    python tools/generate_catalog.py --data .data/transfermarkt --output HiddenSeason.Api/Data/puzzles.json --start-date 2026-07-02 --days 1095

Seçim sabit seed ile deterministiktir. Aynı oyuncu iki gün üst üste gelmez. Bugünün kaydı yoksa API eski oyuncuyu tekrarlamak yerine 503 döndürür.

## Container

    docker build -t hidden-star .
    docker run --rm -p 8080:8080 -v hidden-star-data:/data hidden-star

/data oyuncu ilerlemesini ve Data Protection anahtarlarını kalıcı tutar.

## GitHub Actions ve Azure

.github/workflows/deploy.yml her main push'unda ve pazartesi günleri güncel veriyi indirir, üç yıllık katalog üretir, kontrolleri çalıştırır ve Azure Container Apps'e dağıtır.

Repository secrets:

- AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_SUBSCRIPTION_ID
- AZURE_RESOURCE_GROUP, AZURE_CONTAINER_APP
- GHCR_PAT: read:packages yetkili uzun ömürlü GitHub tokenı

Container App için port 8080, external ingress, minimum replica 0 ve /data yoluna Azure Files volume önerilir. /health endpoint'i probe olarak kullanılabilir.

Veri kaynağı: dcaribou/transfermarkt-datasets (CC0).
