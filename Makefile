all:
	@./scripts/make.sh

install:
	@./scripts/make.sh install

check:
	@./scripts/make.sh check

release:
	@./scripts/make.sh release
	
publish:
	@./scripts/make.sh publish

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
