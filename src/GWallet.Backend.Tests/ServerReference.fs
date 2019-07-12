namespace GWallet.Backend.Tests

open System
open System.Text

open Newtonsoft.Json
open NUnit.Framework

open GWallet.Backend

[<TestFixture>]
type ServerReference() =

    let some_connection_type_irrelevant_for_this_test = { Encrypted = false; Protocol = Http }

    [<Test>]
    member __.``order of servers is kept if non-hostname details are same``() =
        let serverWithHighestPriority =
            {
                HostName = "dlm8yerwlcifs"
                ConnectionType = some_connection_type_irrelevant_for_this_test
                LastSuccessfulCommunication = None
            }
        let serverWithLowestPriority =
            {
                 HostName = "eliuh4midkndk"
                 ConnectionType = some_connection_type_irrelevant_for_this_test
                 LastSuccessfulCommunication = None
             }
        let serverDetails = ServerRegistry.Serialize [ serverWithHighestPriority; serverWithLowestPriority]

        let serverAPos = serverDetails.IndexOf serverWithHighestPriority.HostName
        let serverBPos = serverDetails.IndexOf serverWithLowestPriority.HostName

        Assert.That(serverAPos, Is.Not.LessThan 0)

        Assert.That(serverBPos, Is.Not.LessThan 0)

        Assert.That(serverBPos, Is.GreaterThan serverAPos, "shouldn't be sorted #1")

        let serverDetailsReverse = ServerRegistry.Serialize [ serverWithLowestPriority; serverWithHighestPriority ]

        let serverAPos = serverDetailsReverse.IndexOf serverWithHighestPriority.HostName
        let serverBPos = serverDetailsReverse.IndexOf serverWithLowestPriority.HostName

        Assert.That(serverAPos, Is.Not.LessThan 0)

        Assert.That(serverBPos, Is.Not.LessThan 0)

        Assert.That(serverAPos, Is.GreaterThan serverBPos, "shouldn't be sorted #2")

    [<Test>]
    member __.``order of servers depends on last successful conn``() =
        let serverWithOldestConnection =
            {
                HostName = "dlm8yerwlcifs"
                ConnectionType = some_connection_type_irrelevant_for_this_test
                LastSuccessfulCommunication = Some (DateTime.Now - TimeSpan.FromDays 10.0)
            }
        let serverWithMostRecentConnection =
            {
                 HostName = "eliuh4midkndk"
                 ConnectionType = some_connection_type_irrelevant_for_this_test
                 LastSuccessfulCommunication = Some DateTime.Now
             }
        let serverDetails = ServerRegistry.Serialize [ serverWithOldestConnection; serverWithMostRecentConnection]

        let serverAPos = serverDetails.IndexOf serverWithOldestConnection.HostName
        let serverBPos = serverDetails.IndexOf serverWithMostRecentConnection.HostName

        Assert.That(serverAPos, Is.Not.LessThan 0)

        Assert.That(serverBPos, Is.Not.LessThan 0)

        Assert.That(serverAPos, Is.GreaterThan serverBPos, "should be sorted #1")

        let serverDetailsReverse = ServerRegistry.Serialize [ serverWithMostRecentConnection; serverWithOldestConnection ]

        let serverAPos = serverDetailsReverse.IndexOf serverWithOldestConnection.HostName
        let serverBPos = serverDetailsReverse.IndexOf serverWithMostRecentConnection.HostName

        Assert.That(serverAPos, Is.Not.LessThan 0)

        Assert.That(serverBPos, Is.Not.LessThan 0)

        Assert.That(serverAPos, Is.GreaterThan serverBPos, "should be sorted #2")


        let serverWithNoLastConnection =
            {
                HostName = "dlm8yerwlcifs"
                ConnectionType = some_connection_type_irrelevant_for_this_test
                LastSuccessfulCommunication = None
            }

        let serverDetails3 = ServerRegistry.Serialize [ serverWithNoLastConnection; serverWithMostRecentConnection]

        let serverAPos = serverDetails.IndexOf serverWithNoLastConnection.HostName
        let serverBPos = serverDetails.IndexOf serverWithMostRecentConnection.HostName

        Assert.That(serverAPos, Is.Not.LessThan 0)

        Assert.That(serverBPos, Is.Not.LessThan 0)

        Assert.That(serverAPos, Is.GreaterThan serverBPos, "should be sorted #3")

        let serverDetails3Rev = ServerRegistry.Serialize [ serverWithMostRecentConnection; serverWithNoLastConnection]

        let serverAPos = serverDetails3Rev.IndexOf serverWithNoLastConnection.HostName
        let serverBPos = serverDetails3Rev.IndexOf serverWithMostRecentConnection.HostName

        Assert.That(serverAPos, Is.Not.LessThan 0)

        Assert.That(serverBPos, Is.Not.LessThan 0)

        Assert.That(serverAPos, Is.GreaterThan serverBPos, "should be sorted #4")

    [<Test>]
    member __.``stats of server are included in serialization``() =
        let now = DateTime.UtcNow
        let serverWithSomeRecentConnection =
            {
                 HostName = "eliuh4midkndk"
                 ConnectionType = some_connection_type_irrelevant_for_this_test
                 LastSuccessfulCommunication = Some now
             }
        let serverDetails = ServerRegistry.Serialize [ serverWithSomeRecentConnection ]

        let dayPos = serverDetails.IndexOf (now.Day.ToString())
        Assert.That(dayPos, Is.GreaterThan 0)

        let monthPos = serverDetails.IndexOf (now.Month.ToString())
        Assert.That(monthPos, Is.GreaterThan 0)

        let yearPos = serverDetails.IndexOf (now.Year.ToString())
        Assert.That(yearPos, Is.GreaterThan 0)

        let hourPos = serverDetails.IndexOf (now.Hour.ToString())
        Assert.That(hourPos, Is.GreaterThan 0)

        let minPos = serverDetails.IndexOf (now.Minute.ToString())
        Assert.That(minPos, Is.GreaterThan 0)

    [<Test>]
    member __.``serialization is JSON based (for readability and consistency with rest of wallet)``() =
        let now = DateTime.UtcNow
        let serverWithSomeRecentConnection =
            {
                 HostName = "eliuh4midkndk"
                 ConnectionType = some_connection_type_irrelevant_for_this_test
                 LastSuccessfulCommunication = Some DateTime.UtcNow
             }
        let serverDetails = ServerRegistry.Serialize [ serverWithSomeRecentConnection ]

        let deserializedServerDetails = JsonConvert.DeserializeObject serverDetails
        Assert.That(deserializedServerDetails, Is.Not.Null)

    [<Test>]
    member __.``details of server are included in serialization``() =
        let port = 50001u
        let serverWithSomeRecentConnection =
            {
                 HostName = "eliuh4midkndk"
                 ConnectionType = { Encrypted = false; Protocol = Tcp port }
                 LastSuccessfulCommunication = None
             }
        let serverDetails = ServerRegistry.Serialize [ serverWithSomeRecentConnection ]

        let portPos = serverDetails.IndexOf (port.ToString())
        Assert.That(portPos, Is.GreaterThan 0)

