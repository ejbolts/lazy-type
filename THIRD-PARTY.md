# Third-party components

Lazy Type uses these components locally. No cloud API account is required.

| Component | Source | License |
|---|---|---|
| whisper.cpp b5130 / v1.9.4 | https://github.com/ggml-org/whisper.cpp | MIT |
| Whisper Large V3 Turbo, Q5_0 | https://huggingface.co/ggerganov/whisper.cpp | MIT |
| Silero VAD 6.2 | https://huggingface.co/ggml-org/whisper-vad | MIT |
| llama.cpp b11146 | https://github.com/ggml-org/llama.cpp | MIT |
| Qwen3-4B-Instruct-2507, Q4_K_M conversion | https://huggingface.co/unsloth/Qwen3-4B-Instruct-2507-GGUF | Apache 2.0 |
| Original Qwen model | https://huggingface.co/Qwen/Qwen3-4B-Instruct-2507 | Apache 2.0 |
| NAudio 2.2.1 | https://github.com/naudio/NAudio | MIT |
| NVIDIA CUDA runtime libraries in upstream release archives | https://docs.nvidia.com/cuda/eula/ | NVIDIA CUDA EULA |

The installer pins model revisions and engine releases and verifies their published SHA-256 hashes. Downloads and the installed manifest live in `%USERPROFILE%\Applications\LazyType`, outside this repository. Engine archives retain their supplied notices. Consult upstream licenses before redistributing binaries or models.
