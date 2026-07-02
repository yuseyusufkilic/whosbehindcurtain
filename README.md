# Hidden Star

[![Deploy](https://www.herokucdn.com/deploy/button.svg)](https://heroku.com/deploy?template=https://github.com/yuseyusufkilic/whosbehindcurtain)

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

/data yerel ve Droplet tabanlı kurulumlarda oyuncu ilerlemesini kalıcı tutar. `DATABASE_URL` tanımlandığında ilerleme PostgreSQL'de saklanır.

## GitHub Actions ve Heroku

.github/workflows/deploy.yml her main push'unda güncel veriyi indirir, üç yıllık kataloğu doğrular, web/API kontrollerini ve container build'ini çalıştırır.

Heroku uygulaması Container stack kullanır ve `heroku.yml` üzerinden Dockerfile'ı build eder. Gerekli config vars:

- `DATABASE_URL`: Heroku Postgres eklendiğinde otomatik tanımlanır.
- `SESSION_SIGNING_KEY`: En az 32 karakterlik rastgele ve kalıcı bir secret.
- `Proxy__TrustForwardedHeaders`: `true`

Heroku'nun verdiği `PORT` çalışma anında otomatik kullanılır. `/api/health` health endpoint'idir.

Veri kaynağı: dcaribou/transfermarkt-datasets (CC0).
