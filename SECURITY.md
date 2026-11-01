# Security

MultiNaughtica is a fork of Nitrox and has the same limits: the packets between the game and the server are not authenticated or signed (see [Nitrox issue #1996](https://github.com/SubnauticaNitrox/Nitrox/issues/1996)), and the server relays what a client sends to the other clients. A modified client can therefore send things that harm the others or alter the world. **Play only with people you trust, and set a world password if the server is reachable from the internet.**

This fork has no security team and promises no fixes. If you find a vulnerability, please report it privately to the maintainer of this repository through GitHub, and if it also affects stock Nitrox, to the Nitrox project (see their [security policy](https://github.com/SubnauticaNitrox/Nitrox/blob/master/SECURITY.md)).
