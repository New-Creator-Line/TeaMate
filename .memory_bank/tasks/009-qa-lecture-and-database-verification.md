# Task 009: QA Comprehensive Verification of Long Lecture Transcriptions & Database Integrity

## Objective
Run a comprehensive, automated test and audit of the Fotoprint AI Copilot system with real long lecture audio files and database persistence.

## Scope & Target Audio Files
The user provided long lecture audio files on their Mac:
1. `/Users/test/Downloads/AirPaste/CRM лекция 1.2.m4a` (~46 MB, ~25 minutes)
2. `/Users/test/Desktop/Что НАДА/обсуждение0.m4a` (~59 MB, ~32 minutes)
3. `/Users/test/Downloads/Telegram Desktop/Саё special ultra deluxe secret lecture.m4a` (~52 MB, ~56 minutes)

## Verification Tasks for QA Agent
1. **Long Lecture Audio Transcription (25-60 min) via API**:
   - Send the lecture audio to `http://localhost:8899/api/audio/transcribe?language=ru` (or `/api/meetings/process-audio`).
   - Measure transcription duration, throughput, and memory consumption.
   - Verify that there are NO infinite loops, NO sentence repetition bugs, NO truncated outputs, and NO crashes on 25-60 min files.
   - Verify that segments are generated with sequential timestamps across the entire duration.
2. **Database Integrity & Conversion Audit**:
   - Audit PostgreSQL setup in the project (`infra/postgres/init/02_schema.sql`, connection strings).
   - Investigate whether PostgreSQL is accessible on localhost (port 5432 vs port 5434).
   - Verify how meeting protocols (`meetings`, `meeting_notes`, `meeting_protocols`, `action_items`) are converted and stored into the database.
   - Verify offline fallback safety when PostgreSQL is not connected or in standalone mode.
   - Verify that `.docx` generation and Bitrix24 task payloads are correctly formed from the stored data.
3. **Structured Verification Report**:
   - Deliver an empirical report with exact numbers, execution times, sample transcribed texts, and database status.
