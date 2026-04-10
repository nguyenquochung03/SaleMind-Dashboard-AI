# SalesMind AI - Nền tảng Phân tích Bán hàng Thông minh

SalesMind AI là một ứng dụng web hiện đại dựa trên nền tảng .NET 9, được thiết kế để phân tích dữ liệu bán hàng và cung cấp thông tin chuyên sâu (insights) bằng cách sử dụng trí tuệ nhân tạo (AI). Dự án sử dụng mô hình Clean Architecture để đảm bảo tính mở rộng, bảo trì và kiểm thử dễ dàng.

## 🚀 Tính năng chính

- **Báo cáo Bán hàng**: Tổng hợp dữ liệu từ các nguồn khác nhau (như DummyJSON).
- **AI Insights**: Tích hợp OpenRouter để phân tích hành vi khách hàng và xu hướng thị trường.
- **AI Orchestrator**: Quy trình xử lý đa bước để giải quyết các truy vấn phức tạp về dữ liệu.
- **Caching**: Sử dụng Redis để tối ưu hóa hiệu suất và giảm độ trễ API.
- **Rate Limiting**: Bảo mật và kiểm soát lưu lượng truy cập API.

## 🛠 Công nghệ sử dụng

- **Backend**: ASP.NET Core 9.0 (MVC)
- **Kiến trúc**: Clean Architecture (Domain-Driven Design - DDD)
- **AI**: OpenRouter API Integration
- **Cache**: Redis
- **Containerization**: Docker & Docker Compose
- **Data Source**: DummyJSON (External API)

## 📁 Cấu trúc thư mục

Dự án tuân theo Clean Architecture:

- `src/Domain`: Chứa các thực thể, interface cốt lõi và logic nghiệp vụ cơ bản.
- `src/Application`: Chứa các Use Cases, DTOs và Mapping logic.
- `src/Infrastructure`: Chứa các implementation về persistence, external services (Redis, AI Clients).
- `src/Web`: Lớp giao diện người dùng (Controllers, Views, Static files).

## 💻 Hướng dẫn chạy dự án

### Cách 1: Sử dụng Docker (Khuyến nghị)

Đây là cách nhanh nhất để khởi chạy toàn bộ môi trường bao gồm ứng dụng và Redis.

1.  **Yêu cầu**: Đã cài đặt [Docker Desktop](https://www.docker.com/products/docker-desktop/).
2.  **Khởi chạy**:
    ```bash
    docker-compose up -d
    ```
3.  **Truy cập**: Ứng dụng sẽ chạy tại địa chỉ `http://localhost:8080`.

### Cách 2: Chạy trực tiếp qua .NET CLI

1.  **Yêu cầu**:
    - .NET SDK 9.0
    - Một instance Redis đang chạy (local hoặc cloud).
2.  **Cấu hình**: Cập nhật `Redis__Configuration` và `AI__OpenRouter__ApiKey` trong `src/Web/appsettings.json`.
3.  **Chạy ứng dụng**:
    ```bash
    dotnet restore
    dotnet run --project src/Web/Web.csproj
    ```

## ⚙️ Cấu hình (Environment Variables)

Bạn có thể cấu hình dự án thông qua file `appsettings.json` hoặc biến môi trường:

- `AI__OpenRouter__ApiKey`: API Key từ OpenRouter.
- `Redis__Configuration`: Địa chỉ kết nối Redis (mặc định: `redis:6379` trong Docker).
- `ASPNETCORE_ENVIRONMENT`: Chế độ môi trường (`Development` hoặc `Production`).

## 🚢 Hướng dẫn Deploy

### Triển khai bằng Docker

Dự án đã có sẵn `Dockerfile` đa giai đoạn (multi-stage) tối ưu cho Production.

1.  **Build image**:
    ```bash
    docker build -t salesmind-ai .
    ```
2.  **Deploy**: Bạn có thể đẩy image này lên Docker Hub hoặc AWS ECR/Azure Container Registry và chạy trên các dịch vụ như Kubernetes, Azure Container Apps hoặc VPS với Docker Compose.

### Ví dụ file `.env` cho Production
```env
ASPNETCORE_ENVIRONMENT=Production
AI__OpenRouter__ApiKey=your_real_api_key
Redis__Configuration=your_production_redis_host:6379,password=...
```

---
*Phát triển bởi SalesMind AI Team*
