#!/usr/bin/env bash
set -euo pipefail

LIGHT_MODEL=false
CHECK_ONLY=false

show_help() {
  cat << 'EOF'
Usage: ./scripts/download-model.sh [OPTIONS]

Downloads Whisper model weights for Fotoprint local speech-to-text inference.

Options:
  --light       Download/check lightweight model (ggml-base.bin ~142MB) instead of large-v3-turbo (~1.5GB)
  --check-only  Verify whether the model exists without downloading
  --help, -h    Display this help message and exit
EOF
}

for arg in "$@"; do
  case "$arg" in
    --light)
      LIGHT_MODEL=true
      ;;
    --check-only)
      CHECK_ONLY=true
      ;;
    --help|-h)
      show_help
      exit 0
      ;;
    *)
      echo "Error: Unknown argument: $arg" >&2
      show_help >&2
      exit 1
      ;;
  esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
MODELS_DIR="$REPO_ROOT/models"

if [ "$LIGHT_MODEL" = true ]; then
  MODEL_NAME="ggml-base.bin"
else
  MODEL_NAME="ggml-large-v3-turbo.bin"
fi

SYSTEM_MODEL_DIR="$HOME/Library/Application Support/app.yuko.tsukiko/models"
LOCAL_MODEL_PATH="$MODELS_DIR/$MODEL_NAME"
SYSTEM_MODEL_PATH="$SYSTEM_MODEL_DIR/$MODEL_NAME"

echo "Checking Whisper model: $MODEL_NAME..."

FOUND_PATH=""
if [ -f "$LOCAL_MODEL_PATH" ]; then
  FOUND_PATH="$LOCAL_MODEL_PATH"
elif [ -f "$SYSTEM_MODEL_PATH" ]; then
  FOUND_PATH="$SYSTEM_MODEL_PATH"
fi

if [ -n "$FOUND_PATH" ]; then
  echo -e "\033[32m✔ Whisper model found: $FOUND_PATH\033[0m"
  exit 0
fi

if [ "$CHECK_ONLY" = true ]; then
  echo -e "\033[33m⚠ Model $MODEL_NAME not found (checked $LOCAL_MODEL_PATH and $SYSTEM_MODEL_PATH).\033[0m"
  exit 0
fi

echo "Model not found in local or system cache."
echo "Downloading $MODEL_NAME from HuggingFace..."
mkdir -p "$MODELS_DIR"

HF_URL="https://huggingface.co/ggerganov/whisper.cpp/resolve/main/$MODEL_NAME"
TEMP_FILE="$LOCAL_MODEL_PATH.tmp"

if command -v curl >/dev/null 2>&1; then
  curl -L --progress-bar -f "$HF_URL" -o "$TEMP_FILE"
elif command -v wget >/dev/null 2>&1; then
  wget --show-progress -O "$TEMP_FILE" "$HF_URL"
else
  echo "Error: Neither curl nor wget is available." >&2
  exit 1
fi

mv "$TEMP_FILE" "$LOCAL_MODEL_PATH"
echo -e "\033[32m✔ Successfully downloaded $MODEL_NAME to $LOCAL_MODEL_PATH\033[0m"
exit 0
