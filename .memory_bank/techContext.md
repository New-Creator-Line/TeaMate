# Tech Context

- **Runtime & Language**: .NET 9 (`net9.0`), C# 13, Nullable Reference Types enabled, ImplicitUsings enabled.
- **Web Framework**: ASP.NET Core Minimal APIs (`Fotoprint.AiGateway.Api`).
- **Database**: PostgreSQL 16 + `pgvector`, accessed via `Dapper` and `Npgsql` (`FotoprintRepository`).
- **Configuration**: `YamlDotNet` deserializing `config.yaml` with `UnderscoredNamingConvention` into `FotoprintConfig`.
- **Document Generation**: `DocumentFormat.OpenXml` in `MeetingProtocolGenerator`.
- **Local STT Engine**: Tsukiko 1.4.4 (`/Applications/tsukiko.app/Contents/Helpers/tsukiko-transcribe` & `tsukiko-recognizer`), Metal GPU acceleration, model `ggml-large-v3-turbo.bin`.
- **Cloud LLM & STT Fallback**: Google Gemini (`gemini-1.5-flash`) via REST `v1beta/models/{model}:generateContent`.
- **Observability**: Langfuse v3 (`ILangfuseClient`).
- **Testing**: xUnit (`tests/Fotoprint.Tests`).
