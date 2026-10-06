# ==============================================================================
# Makefile para DeskPets (Cross-platform: Windows, Linux e macOS)
# ==============================================================================

# Detecção do Sistema Operacional e configurações específicas
ifeq ($(OS),Windows_NT)
	DETECTED_OS := Windows
	DEFAULT_RID := win-x64
	DOTNET_VER  := $(shell dotnet --version)
	BLANK       := echo.
else
	UNAME_S     := $(shell uname -s 2>/dev/null || echo Linux)
	ifeq ($(UNAME_S),Darwin)
		DETECTED_OS := macOS
		DEFAULT_RID := osx-x64
	else
		DETECTED_OS := Linux
		DEFAULT_RID := linux-x64
	endif
	DOTNET_VER  := $(shell dotnet --version 2>/dev/null || echo "Desconhecido")
	BLANK       := echo ""
endif

# Variáveis configuráveis (podem ser sobrescritas via argumentos)
# Exemplos:
#   make run
#   make debug
#   make run PROJECT=DeskPets.Core/DeskPets.Core.csproj
#   make run CONFIG=Release
DOTNET      ?= dotnet
PROJECT     ?= DeskPets.Ui/DeskPets.Ui.csproj
CONFIG      ?= Debug
RID         ?= $(DEFAULT_RID)

.DEFAULT_GOAL := help

.PHONY: all help info restore build build-debug build-release run debug release watch clean clean-all publish publish-self test

# ------------------------------------------------------------------------------
# Ajuda e Informações
# ------------------------------------------------------------------------------

## Exibe menu de comandos e ajuda
help:
	@echo ==================================================================
	@echo  DeskPets - Makefile ($(DETECTED_OS))
	@echo ==================================================================
	@echo Uso: make [comando] [PROJECT=...] [CONFIG=...]
	@$(BLANK)
	@echo Comandos para Rodar e Debugar:
	@echo   make run           - Executa o app (padrao: $(CONFIG))
	@echo   make debug         - Executa em modo Debug (com DevTools do Avalonia)
	@echo   make release       - Executa em modo Release
	@echo   make watch         - Executa com Hot Reload (dotnet watch)
	@$(BLANK)
	@echo Comandos de Compilacao:
	@echo   make build         - Compila o projeto ($(CONFIG))
	@echo   make build-debug   - Compila em modo Debug
	@echo   make build-release - Compila em modo Release
	@$(BLANK)
	@echo Comandos Utilitarios:
	@echo   make restore       - Restaura as dependencias do NuGet
	@echo   make clean         - Limpa arquivos de build (dotnet clean)
	@echo   make clean-all     - Limpeza profunda (remove pastas bin e obj)
	@echo   make test          - Executa testes unitarios (se houver)
	@echo   make publish       - Publica aplicacao para $(RID) (framework-dependent)
	@echo   make publish-self  - Publica executavel unico autocontido (single-file)
	@echo   make info          - Exibe informacoes do ambiente e variaveis
	@echo ==================================================================

## Exibe variáveis de ambiente e configurações detectadas
info:
	@echo ==================================================================
	@echo Informacoes do Ambiente:
	@echo ==================================================================
	@echo SO Detectado    : $(DETECTED_OS)
	@echo Versao .NET     : $(DOTNET_VER)
	@echo RID Padrao      : $(RID)
	@echo Projeto Atual   : $(PROJECT)
	@echo Configuracao    : $(CONFIG)
	@echo ==================================================================

# ------------------------------------------------------------------------------
# Dependências e Compilação
# ------------------------------------------------------------------------------

## Restaura dependências do NuGet
restore:
	$(DOTNET) restore $(PROJECT)

## Compila o projeto com a configuração atual ($(CONFIG))
build:
	$(DOTNET) build $(PROJECT) -c $(CONFIG)

## Compila explicitamente em modo Debug
build-debug:
	$(DOTNET) build $(PROJECT) -c Debug

## Compila explicitamente em modo Release
build-release:
	$(DOTNET) build $(PROJECT) -c Release

# ------------------------------------------------------------------------------
# Execução e Depuração
# ------------------------------------------------------------------------------

## Executa o projeto configurado (padrão: Debug)
run:
	$(DOTNET) run --project $(PROJECT) -c $(CONFIG)

## Atalho direto para rodar/debugar em modo Debug (habilita F12 DevTools no Avalonia)
debug:
	$(DOTNET) run --project $(PROJECT) -c Debug

## Atalho para rodar diretamente em modo Release
release:
	$(DOTNET) run --project $(PROJECT) -c Release

## Executa com Hot Reload ativo (ótimo para desenvolvimento visual da UI)
watch:
	$(DOTNET) watch --project $(PROJECT) run

# ------------------------------------------------------------------------------
# Testes e Limpeza
# ------------------------------------------------------------------------------

## Executa testes do projeto
test:
	$(DOTNET) test

## Limpeza simples de compilação
clean:
	$(DOTNET) clean $(PROJECT)

## Limpeza profunda de pastas bin/ e obj/ (compatível com Windows e Linux)
clean-all: clean
ifeq ($(OS),Windows_NT)
	@powershell -NoProfile -Command "Get-ChildItem -Include bin,obj -Recurse | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue"
	@echo Pastas bin e obj removidas com sucesso (Windows).
else
	@find . -type d \( -name bin -o -name obj \) -exec rm -rf {} + 2>/dev/null || true
	@echo Pastas bin e obj removidas com sucesso (Linux/macOS).
endif

# ------------------------------------------------------------------------------
# Publicação / Distribuição
# ------------------------------------------------------------------------------

## Publica versão dependente de framework para a plataforma de destino
publish:
	$(DOTNET) publish $(PROJECT) -c Release -r $(RID) --self-contained false -o publish/$(RID)

## Publica executável único e autocontido (não requer .NET instalado no destino)
publish-self:
	$(DOTNET) publish $(PROJECT) -c Release -r $(RID) --self-contained true -p:PublishSingleFile=true -o publish/$(RID)-standalone
