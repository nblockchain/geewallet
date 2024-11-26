all:
	@./scripts/make.sh

install:
	@./scripts/make.sh install

check:
	@./scripts/make.sh check

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

push:
	@./scripts/make.sh push

sanitycheck:
	@./scripts/make.sh sanitycheck

strict:
	@./scripts/make.sh strict
