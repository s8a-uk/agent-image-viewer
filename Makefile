SOLUTION    := AgentImageViewer.slnx
PROJECT     := src/AgentImageViewer
DIST        := dist
EXE         := AgentImageViewer.exe
# Asked from Windows only when an install target needs it (recursive variables), so build and test work without it.
# cmd.exe runs from C:\ to avoid its warning about a UNC working directory.
LOCALAPPDATA_WIN = $(shell cd /mnt/c && cmd.exe /c "echo %LOCALAPPDATA%" 2>/dev/null | tr -d '\r')
INSTALL_DIR ?= $(shell wslpath -u '$(LOCALAPPDATA_WIN)')/Programs/AgentImageViewer
INSTALL_EXE  = $(INSTALL_DIR)/$(EXE)

DOTNET := docker compose exec -T dotnet dotnet

.PHONY: up down build test test-hooks format format-check publish deploy install stop start clean

up:
	docker compose up -d

down:
	docker compose down

build: up
	$(DOTNET) build $(SOLUTION) -c Debug

# Builds everything but runs only the Core tests here: the UI tests need Windows and run in CI.
test: up test-hooks
	$(DOTNET) test --solution $(SOLUTION)

# The hook is plain Python 3 (standard library only); its tests run in a throwaway container.
test-hooks:
	docker run --rm -u "$$(id -u):$$(id -g)" -v "$(CURDIR)/hooks:/hooks:ro" -w /hooks \
		-e PYTHONDONTWRITEBYTECODE=1 python:3.12-slim python -m unittest discover -s tests -v

format: up
	$(DOTNET) format $(SOLUTION)

format-check: up
	$(DOTNET) format $(SOLUTION) --verify-no-changes

publish: up
	$(DOTNET) publish $(PROJECT) -c Release -r win-x64 --self-contained false \
		-p:PublishSingleFile=true \
		-p:DebugType=none -o $(DIST)

# Build, stop the running viewer, overwrite the exe and start it again.
deploy: publish stop install start

install:
	mkdir -p "$(INSTALL_DIR)"
	cp $(DIST)/$(EXE) "$(INSTALL_EXE)"
	@echo "Installed: $(INSTALL_EXE)"

# "quit" waits until the process has exited, so the exe is unlocked afterwards.
# If it fails (e.g. an old or hung viewer), force-kill so the deploy can still overwrite the exe.
stop:
	@if [ -f "$(INSTALL_EXE)" ]; then "$(INSTALL_EXE)" quit || taskkill.exe /IM $(EXE) /F >/dev/null 2>&1 || true; fi

start:
	powershell.exe -NoProfile -Command "Start-Process -FilePath '$(shell wslpath -w "$(INSTALL_EXE)" 2>/dev/null)'"

clean:
	rm -rf $(DIST)
	find src tests -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
