# Project Brief: Fotoprint AI Copilot — Audio Transcription & MoM Automation (Track #3)

## Overview
Fotoprint AI Copilot is an enterprise AI secretary for C-Level executives and department heads at LLC "Fotoprint". This initiative adds resilient, hybrid local/cloud Speech-to-Text (STT) transcription, Telegram voice/audio message ingestion, and automated end-to-end Minutes of Meeting (MoM) generation with Word `.docx` export and Human-in-the-Loop Bitrix24 task creation.

## Core Objectives
1. **On-Premise First Audio Transcription**: Prioritize local Apple Silicon Metal-accelerated `Tsukiko` (`whisper.cpp` with `ggml-large-v3-turbo.bin`) for zero-cost, offline transcription of corporate audio.
2. **Automatic Cloud Fallback**: Transparently fall back to Google Gemini Audio API (`gemini-1.5-flash`) when running in environments without Tsukiko (e.g., Docker containers or Windows evaluation machines).
3. **Human-in-the-Loop (HITL)**: Extract action items from meeting transcripts and present interactive Telegram approval buttons (`create_bitrix_task:<id>`) without mutating Bitrix24 automatically.
4. **Zero Hallucinations ("требует уточнения")**: Strictly mark any missing responsible person or deadline in extracted action items or unclear points as `"требует уточнения"`.
5. **Official MoM Export**: Generate structured `.docx` protocols (Decisions, Discussed, Risks, Action Items) via `IMeetingProtocolGenerator`.
