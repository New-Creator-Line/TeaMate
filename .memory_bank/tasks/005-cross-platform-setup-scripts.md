# Task 005: In-Repo C++ Inference Engines (`tools/engine/`) & Model-Only Download Scripts (`scripts/download-model.*`)

## 1. Scope & Assigned Worktree
- **Target Branch**: `worktree/005-setup-scripts`
- **Role**: DevOps Script Developer (`coder-worker`)

## 2. File Boundaries (Strict Allowlist)
You may ONLY create, modify, or delete files within:
- `tools/engine/macos/tsukiko-recognizer`
- `tools/engine/win/tsukiko-recognizer-vulkan.exe`
- `tools/engine/win/tsukiko-recognizer-cpu.exe`
- `tools/engine/win/ffmpeg.exe`
- `scripts/download-model.sh`
- `scripts/download-model.ps1`
- `scripts/setup-tsukiko-mac.sh` (DELETE via `git rm`)
- `scripts/setup-tsukiko-win.ps1` (DELETE via `git rm`)
- `.gitignore`

Do NOT touch `src/**`, `tests/**`, `config.yaml`, `config/vocabulary.yaml`, or `README.md`.

## 3. Frozen Contract Reference
Read and strictly implement `.memory_bank/contracts/audio_transcription_contract.md` (v2.0 Section 3).

## 4. Implementation Requirements
1. **Delete Legacy Tsukiko App Setup Scripts**:
   - Remove `scripts/setup-tsukiko-mac.sh` and `scripts/setup-tsukiko-win.ps1` using `git rm`.
2. **Copy Standalone C++ Inference Binaries into `tools/engine/`**:
   - Create `tools/engine/macos/` and `tools/engine/win/`.
   - Copy `/Applications/tsukiko.app/Contents/Helpers/tsukiko-recognizer` (6.7 MB) to `tools/engine/macos/tsukiko-recognizer` and ensure `chmod +x tools/engine/macos/tsukiko-recognizer`.
   - Copy `/tmp/tsukiko_win/tsukiko/Engine/tsukiko-recognizer-vulkan.exe` (54 MB) to `tools/engine/win/tsukiko-recognizer-vulkan.exe`.
   - Copy `/tmp/tsukiko_win/tsukiko/Engine/tsukiko-recognizer-cpu.exe` (1.8 MB) to `tools/engine/win/tsukiko-recognizer-cpu.exe`.
   - Copy `/tmp/tsukiko_win/tsukiko/Engine/ffmpeg.exe` (2.7 MB) to `tools/engine/win/ffmpeg.exe`.
3. **Create Model-Only Download Scripts (`scripts/download-model.sh` & `scripts/download-model.ps1`)**:
   - Neither script may download or install the Tsukiko application! They ONLY verify/download the Whisper `.bin` model weights.
   - `scripts/download-model.sh` (executable `chmod +x`):
     - Supports `--help`, `--light` (selects `ggml-base.bin` instead of `ggml-large-v3-turbo.bin`), and `--check-only` (checks if model exists and exits `0` without downloading).
     - Rejects any unknown CLI flag by printing error + usage to `stderr` (`>&2`) and exiting with `exit 1`.
     - Checks if the target model (`.bin`) already exists in `./models/` or `$HOME/Library/Application Support/app.yuko.tsukiko/models/`.
     - If found, prints `[OK] Whisper model found at: <path> (no download needed)` and exits `0`.
     - If not found (and `--check-only` is not set), creates `./models/` and downloads `https://huggingface.co/ggerganov/whisper.cpp/resolve/main/<model_name>` via `curl -L --fail --progress-bar -o "./models/<model_name>"`.
   - `scripts/download-model.ps1`:
     - Uses `[CmdletBinding(PositionalBinding = $false)]` with `param([switch]$LightModel, [switch]$CheckOnly, [switch]$Help)`.
     - Checks `./models/` and `$env:APPDATA\app.yuko.tsukiko\models\`.
     - If missing (and not `-CheckOnly`), creates `./models/` and downloads the `.bin` model from HuggingFace (`https://huggingface.co/ggerganov/whisper.cpp/resolve/main/<model_name>`).
4. **Update `.gitignore`**:
   - Ensure `models/*.bin` is ignored in `.gitignore`.
   - Ensure `tools/engine/` is NOT ignored by `.gitignore` so the 4 C++ engine binaries are tracked and committed in Git.

## 5. Acceptance Criteria
1. `scripts/setup-tsukiko-mac.sh` and `scripts/setup-tsukiko-win.ps1` are deleted.
2. `tools/engine/macos/tsukiko-recognizer`, `tools/engine/win/tsukiko-recognizer-vulkan.exe`, `tools/engine/win/tsukiko-recognizer-cpu.exe`, and `tools/engine/win/ffmpeg.exe` are committed in Git.
3. `bash -n scripts/download-model.sh` passes, `bash scripts/download-model.sh` detects the existing model on this Mac without downloading anything and exits `0`, and `bash scripts/download-model.sh --invalid-flag` exits `1`.
4. `git check-ignore -v models/ggml-large-v3-turbo.bin` exits `0`.
