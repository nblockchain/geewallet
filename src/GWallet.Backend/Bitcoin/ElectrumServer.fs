namespace GWallet.Backend.Bitcoin

open GWallet.Backend

type internal ElectrumServerPorts =
    {
        InsecurePort: Option<int>;
        SecurePort: int;
    }
    static member Default () =
        { InsecurePort = Some(50001); SecurePort = 50002 }


type internal ElectrumServer =
    {
        Host: string;
        Ports: ElectrumServerPorts;
    }

module internal ElectrumServerSeedList =

        // list taken from https://github.com/spesmilo/electrum/blob/master/lib/network.py#L53
    let private defaultList =
        [
            { Host = "erbium1.sytes.net"; Ports = ElectrumServerPorts.Default() };                                           // core, e-x
            { Host = "ecdsa.net"; Ports = { InsecurePort = ElectrumServerPorts.Default().InsecurePort; SecurePort = 110 } }; // core, e-x
            { Host = "gh05.geekhosters.com"; Ports = ElectrumServerPorts.Default() };                                        // core, e-x
            { Host = "VPS.hsmiths.com"; Ports = ElectrumServerPorts.Default() };                                             // core, e-x
            { Host = "electrum.anduck.net"; Ports = ElectrumServerPorts.Default() };                                         // core, e-s; banner with version pending
            { Host = "electrum.no-ip.org"; Ports = ElectrumServerPorts.Default() };                                          // core, e-s
            { Host = "electrum.be"; Ports = ElectrumServerPorts.Default() };                                                 // core, e-x
            { Host = "helicarrier.bauerj.eu"; Ports = ElectrumServerPorts.Default() };                                       // core, e-x
            { Host = "elex01.blackpole.online"; Ports = ElectrumServerPorts.Default() };                                     // core, e-x
            { Host = "electrumx.not.fyi"; Ports = ElectrumServerPorts.Default() };                                           // core, e-x
            { Host = "node.xbt.eu"; Ports = ElectrumServerPorts.Default() };                                                 // core, e-x
            { Host = "kirsche.emzy.de"; Ports = ElectrumServerPorts.Default() };                                             // core, e-x
            { Host = "electrum.villocq.com"; Ports = ElectrumServerPorts.Default() };                                        // core?, e-s; banner with version recommended
            { Host = "us11.einfachmalnettsein.de"; Ports = ElectrumServerPorts.Default() };                                  // core, e-x
            { Host = "electrum.trouth.net"; Ports = ElectrumServerPorts.Default() };                                         // BU, e-s
            { Host = "Electrum.hsmiths.com"; Ports = { InsecurePort = Some(8080); SecurePort = 995 } };                      // core, e-x
            { Host = "electrum3.hachre.de"; Ports = ElectrumServerPorts.Default() };                                         // core, e-x
            { Host = "b.1209k.com"; Ports = ElectrumServerPorts.Default() };                                                 // XT, jelectrum
            { Host = "elec.luggs.co"; Ports = { InsecurePort = None; SecurePort = 443 } };                                   // core, e-x
            { Host = "Electrum.hsmiths.com"; Ports = { InsecurePort = Some(110); SecurePort = 995 } };                       // BU, e-x
        ]

    let Randomize() =
        Shuffler.Unsort defaultList
