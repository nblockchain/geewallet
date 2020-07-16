all:
	@./scripts/make.sh

install:
	@./scripts/make.sh install

check:
	@./scripts/make.sh check

check-end-to-end:
	@./scripts/make.sh check-end-to-end

release:
	@./scripts/make.sh release

zip:
	@./scripts/make.sh zip

run:
	@./scripts/make.sh run

update-servers:
	@./scripts/make.sh update-servers

nuget:
	@./scripts/make.sh nuget

sanitycheck:
	@./scripts/make.sh sanitycheck

strict:
	@./scripts/make.sh strict
