namespace GWallet.Backend.Tests

open System
open System.IO

open NUnit.Framework

open GWallet.Backend

[<TestFixture>]
type Config() =

    let CreateTempDir (): DirectoryInfo =
        let tempDirPath = Path.Combine(Path.GetTempPath(), "gwallet-tests-" + Guid.NewGuid().ToString())
        let tempDir = DirectoryInfo tempDirPath
        tempDir.Create()
        tempDir

    let CreateAccountFile (configDir: DirectoryInfo) (fileName: string): unit =
        let accountsDir = DirectoryInfo(Path.Combine(configDir.FullName, "accounts", "normal", "BTC"))
        accountsDir.Create()
        File.WriteAllText(Path.Combine(accountsDir.FullName, fileName), "some account data")

    let AssertMigratedOldDir (migratedOldConfigDir: Option<DirectoryInfo>) (expectedOldConfigDir: DirectoryInfo) =
        match migratedOldConfigDir with
        | Some migratedOldConfigDir ->
            Assert.That(migratedOldConfigDir.FullName, Is.EqualTo expectedOldConfigDir.FullName)
        | None ->
            Assert.Fail "expected an old config dir migration to happen"

    [<Test>]
    member __.``old config dir gets copied to new config dir when the latter has no existing config``() =
        let oldConfigDir = CreateTempDir ()
        let newConfigDir = CreateTempDir ()
        try
            CreateAccountFile oldConfigDir "someAccount.json"

            let migratedOldConfigDir = Config.MaybeMigrateOldConfigDir newConfigDir (Seq.singleton oldConfigDir)

            AssertMigratedOldDir migratedOldConfigDir oldConfigDir

            let migratedAccountFile =
                FileInfo(Path.Combine(newConfigDir.FullName, "accounts", "normal", "BTC", "someAccount.json"))
            Assert.That(migratedAccountFile.Exists, Is.True)
            Assert.That(File.ReadAllText migratedAccountFile.FullName, Is.EqualTo "some account data")
        finally
            oldConfigDir.Delete true
            newConfigDir.Delete true

    [<Test>]
    member __.``no migration happens when the new config dir already has a config``() =
        let oldConfigDir = CreateTempDir ()
        let newConfigDir = CreateTempDir ()
        try
            CreateAccountFile oldConfigDir "someOldAccount.json"
            CreateAccountFile newConfigDir "someNewAccount.json"

            let migratedOldConfigDir = Config.MaybeMigrateOldConfigDir newConfigDir (Seq.singleton oldConfigDir)

            match migratedOldConfigDir with
            | Some _ ->
                Assert.Fail "expected no old config dir migration to happen"
            | None ->
                ()

            // the existing config of the new config dir doesn't get overwritten:
            let newAccountFile =
                FileInfo(Path.Combine(newConfigDir.FullName, "accounts", "normal", "BTC", "someNewAccount.json"))
            Assert.That(File.ReadAllText newAccountFile.FullName, Is.EqualTo "some account data")

            // and the config of the old config dir doesn't get copied over:
            let oldAccountFileInNewConfigDir =
                FileInfo(Path.Combine(newConfigDir.FullName, "accounts", "normal", "BTC", "someOldAccount.json"))
            Assert.That(oldAccountFileInNewConfigDir.Exists, Is.False)
        finally
            oldConfigDir.Delete true
            newConfigDir.Delete true

    [<Test>]
    member __.``no migration happens when no old config dir exists``() =
        let oldConfigDir = CreateTempDir ()
        oldConfigDir.Delete ()
        let newConfigDir = CreateTempDir ()
        try
            let migratedOldConfigDir = Config.MaybeMigrateOldConfigDir newConfigDir (Seq.singleton oldConfigDir)

            match migratedOldConfigDir with
            | Some _ ->
                Assert.Fail "expected no old config dir migration to happen"
            | None ->
                ()

            let accountsDir = DirectoryInfo(Path.Combine(newConfigDir.FullName, "accounts"))
            Assert.That(accountsDir.Exists, Is.False)
        finally
            newConfigDir.Delete true

    [<Test>]
    member __.``first existing old config dir candidate is the one that gets migrated``() =
        let nonExistingOldConfigDir = CreateTempDir ()
        nonExistingOldConfigDir.Delete ()
        let oldConfigDir = CreateTempDir ()
        let newConfigDir = CreateTempDir ()
        try
            CreateAccountFile oldConfigDir "someAccount.json"

            let migratedOldConfigDir =
                Config.MaybeMigrateOldConfigDir newConfigDir [nonExistingOldConfigDir; oldConfigDir]

            AssertMigratedOldDir migratedOldConfigDir oldConfigDir
        finally
            oldConfigDir.Delete true
            newConfigDir.Delete true

    [<Test>]
    member __.``no migration happens when the only old config dir candidate is the new config dir itself``() =
        let newConfigDir = CreateTempDir ()
        try
            // this can happen on Linux, where the ApplicationData folder is ~/.config too:
            let migratedOldConfigDir = Config.MaybeMigrateOldConfigDir newConfigDir (Seq.singleton newConfigDir)

            match migratedOldConfigDir with
            | Some _ ->
                Assert.Fail "expected no old config dir migration to happen"
            | None ->
                ()
        finally
            newConfigDir.Delete true

    [<Test>]
    member __.``files of old config dir that are not accounts also get migrated``() =
        let oldConfigDir = CreateTempDir ()
        let newConfigDir = CreateTempDir ()
        try
            CreateAccountFile oldConfigDir "someAccount.json"
            File.WriteAllText(Path.Combine(oldConfigDir.FullName, "someOtherFile.json"), "some other data")

            let migratedOldConfigDir = Config.MaybeMigrateOldConfigDir newConfigDir (Seq.singleton oldConfigDir)

            AssertMigratedOldDir migratedOldConfigDir oldConfigDir
            let migratedOtherFile = FileInfo(Path.Combine(newConfigDir.FullName, "someOtherFile.json"))
            Assert.That(migratedOtherFile.Exists, Is.True)
            Assert.That(File.ReadAllText migratedOtherFile.FullName, Is.EqualTo "some other data")
        finally
            oldConfigDir.Delete true
            newConfigDir.Delete true
