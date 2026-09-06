# CNC stock foundation

Source: `5ac7d045c51194edd9e64d8564f1b726b001be34`.

This slice copies the final source CNC quotation configuration, material catalog,
and stock selection modules without changing their calculation behavior.
It begins implementation of source commits `df7f405f0fcb0baa20de7fa98d24aa057f7710f5`
and `33ce7e2b3351207c726ff5ea8d043aeb48aa6d2d`, incorporating subsequent source
changes in these modules. Both source commits remain pending because their
page integration and full browser tests have not yet been migrated.

The source paths map directly from `Maliev.Web/wwwroot/src/app/js/cnc-quotation/`
to `Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/`.

Regression coverage verifies the source 6061 procurement calculation, one shipping
charge per order, and evidence-based round-stock selection. Tests run in the
existing browser-module CI command. These checks do not claim live page parity.
