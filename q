[33mcommit a28391eea35974339914a681bf5e16795b5c7578[m[33m ([m[1;36mHEAD[m[33m -> [m[1;32mmaster[m[33m)[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 15:57:28 2026 +0100

    Fix Linux image build and CrowdSec LAPI auth

[33mcommit 607eb78b0ab58cfb3d216850f57070e1b29da33e[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 10:54:51 2026 +0100

    Supervise CrowdSec and manager processes

[33mcommit c0e81bcae019aa16e4e7587c507ca7508b28d771[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 10:47:04 2026 +0100

    Fix CrowdSec container initialization and persist bouncer key

[33mcommit d2d4df733a26452e830ac0bc603322ed02c05803[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 10:40:32 2026 +0100

    Test deployment script privilege guards

[33mcommit 151a4442442459c2ce8317a3074ade5823da840a[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 10:40:10 2026 +0100

    Add shell test for CrowdSec entrypoint setup

[33mcommit d176f92f2ebc6c0ce201ba9ed5e3cbe183bcaaff[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 10:38:19 2026 +0100

    Record local CrowdSec deployment constraints

[33mcommit c27aa0df0437368c9c034fae98e490ba9d1ba0f4[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 10:36:07 2026 +0100

    Test login antiforgery and redirect safety

[33mcommit 55bb373788e8326dbe68fac88dcf9034ed49906f[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 10:34:49 2026 +0100

    Move authentication to antiforgery Razor Pages

[33mcommit 7d8dcdc6ccccc9f92197ab0eeb303f9ae612b126[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 10:32:08 2026 +0100

    Propagate LAPI request cancellation

[33mcommit d28b1960c7f58de05cc8cf1a56f38448812a227d[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 10:31:16 2026 +0100

    Timeout threat map background fetch

[33mcommit 1f18962c60b90368d933e562bc9434319ccac06e[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 10:30:26 2026 +0100

    Bound browser threat map state

[33mcommit 69fbd0568531efd926f65941d5c58c4139177b0a[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 10:27:56 2026 +0100

    Replay threats received during map initialization

[33mcommit f4a06c7abf5d894214b3ceb8d20c40243d799297[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 10:26:20 2026 +0100

    Test threat history retention cleanup

[33mcommit decf1624d7e9590443b94e69ec174d98cb5862bc[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 10:25:29 2026 +0100

    Retry transient threat history writes

[33mcommit 80411aa3f7f21ca42380301eafebab1f62bf55de[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 10:24:19 2026 +0100

    Propagate cancellation during threat history writes

[33mcommit 25fbed35a476dfac43a8f654d1fc4c3ff95f18fd[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 10:23:22 2026 +0100

    Reject ambiguous decision address formats

[33mcommit b5ed258f26018f4aae853f85d40ecb830bc00bd9[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 10:22:13 2026 +0100

    Test CrowdSec config and reload process actions

[33mcommit ce6937504c1ef76e0857756ca8a30fa77389e49e[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 10:21:00 2026 +0100

    Validate decision IP and CIDR targets

[33mcommit 2d8856612457eb886d7640070dcd9b524fa59ebc[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 10:20:11 2026 +0100

    Show engine validation errors from stdout

[33mcommit d523292aa6c1e53257bd738d902c6f1b4c6c4706[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 10:19:20 2026 +0100

    Record isolated CrowdSec config validation

[33mcommit 6955b6eb7323fe2dfc4769422ed3c3cf4d7999b4[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 10:17:58 2026 +0100

    Record deployment script and CrowdSec checks

[33mcommit 120afe3d235569f13f32d24f2848815e124e841a[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 10:17:03 2026 +0100

    Test hosted threat feed polling loop

[33mcommit d34ce154ff689b3e8b01fc9f013269681e6a8f2a[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 10:15:54 2026 +0100

    Record Release verification results

[33mcommit 33ca3fcb4b0a4a1bcadbff490fad4d439a2e133b[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 10:14:22 2026 +0100

    Fix deterministic IPv6-aware GeoIP fallback

[33mcommit c04fc9cc5046ccb9bbf6cff1683d99324bbea04a[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 10:13:12 2026 +0100

    Persist threat feed deduplication across restarts

[33mcommit 279bb5e052b875539f829a48dbdbaa86251790c1[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 10:12:22 2026 +0100

    Fail fast when administrator seeding fails

[33mcommit 6cb467d3e113ecaf9a6a274dac28aad131e12eab[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 10:11:14 2026 +0100

    Remove default Quadlet admin password

[33mcommit 91a50591dd096c9f1b0a4e385414a80eff9333fc[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 10:10:15 2026 +0100

    Record verified release publish step

[33mcommit ed7d42a23e7920275c5f83342aa0b2477d172350[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 10:09:57 2026 +0100

    Fix container environment credential binding

[33mcommit d27c1ae4ed4059a0b50f45908ed793d7bcad7eb2[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 10:07:30 2026 +0100

    Add HTTP and identity integration tests

[33mcommit fefee0826f50c7bfe1dd9338535d6b29fc08efc8[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 10:05:02 2026 +0100

    Harden CrowdSec config editing and backup

[33mcommit 7a9cd4d4cc44fe731a695b1143b76db668135f14[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 10:02:08 2026 +0100

    Test CrowdSec adapters and polling paths

[33mcommit ceb36b1c11348b169d56c1a26222f09e3edd08a6[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 09:58:20 2026 +0100

    Fix CrowdSec Hub catalog parsing

[33mcommit fd198176b80f2b0da079c2310e4448928f203cd3[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 09:56:28 2026 +0100

    Add persistence and threat feed tests

[33mcommit 28f99c1d96313b0f800d6aa3e12ad9808a652c51[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 09:53:10 2026 +0100

    Repair CrowdSec Manager and add tests

[33mcommit 1d0b1992ce937723b1e08118d5778f8ab6039acb[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 09:38:41 2026 +0100

    update todo

[33mcommit 5f69f5d8b56dbc559283a689d76412ae3e4b7a02[m
Author: Jordan Bertieaux <bertieaux.jo@gmail.com>
Date:   Sat Oct 31 09:31:24 2026 +0100

    Initialize CrowdSec Manager and fix build
