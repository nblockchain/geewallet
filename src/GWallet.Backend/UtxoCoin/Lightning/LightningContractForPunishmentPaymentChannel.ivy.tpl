IF PUSH(channelCloserPartyPublicKey) CHECKSIGVERIFY
    PUSH(revocationTimeWindow) CHECKSEQUENCEVERIFY
    DROP 1
 ELSE
    PUSH(potentialPunisherPartyPublicKey) CHECKSIGVERIFY
    SHA256 PUSH(channelCloserSecret) EQUAL
 ENDIF
