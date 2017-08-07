/* tslint:disable:no-var-requires no-string-literal */

// jQuery
require("script-loader!jquery");

// Init Bootstrap
require("script-loader!tether");
require("script-loader!bootstrap/js/dist/util.js");
require("script-loader!bootstrap/js/dist/collapse.js");
require("script-loader!bootstrap/js/dist/dropdown.js");
require("script-loader!bootstrap/js/dist/alert.js");
require("script-loader!bootstrap/js/dist/tooltip.js");
require("script-loader!bootstrap/js/dist/modal.js");

$("html").removeClass("pre-init");

// Browser Compat check
require("script-loader!../Vendor/js/modernizr-custom.js");

if ((!(Modernizr.flexbox || Modernizr.flexboxtweener)) ||
	!Modernizr.backgroundsize) {
	$("div#upgradeBrowser").show();
	throw "Please upgrade to a supported browser!";
}

// cookie consent
require("cookieconsent/build/cookieconsent.min.js");

//import {IRuntimeEnvironment} from "./ServerSide/ViewModels";
//declare var runtimeEnvironment: IRuntimeEnvironment;

(<any> window)["cookieconsent"].initialise({
	container: document.body,
	palette: {
		popup: { background: "#237afc" },
		button: { background: "transparent", border: "#fff", text: "#fff" },
	},
	revokable: false,
	law: {
		regionalLaw: false,
	},
	location: false,
});

// Scrolling: navbar transitions
let win = $(window);
let topBar = $("nav.navbar");
let isScrolling = false;

function applyScollingBehavior() {
	if (win.scrollTop() >= 1) {
		if (!isScrolling) {
			topBar.addClass("scrolling");

			isScrolling = true;
		}
	} else {
		if (isScrolling) {
			topBar.removeClass("scrolling");

			isScrolling = false;
		}
	}
}

applyScollingBehavior();    // initial apply for case where page is reloaded while scrolled

win.scroll(() => {
	applyScollingBehavior();
});

// transition hero navbar to scrolling state if mobile menu is triggered while not scrolling to avoid transparent menu
$("#navbarResponsive").on("show.bs.collapse", () => {
	if (!isScrolling && topBar.hasClass("hero")) {
		topBar.addClass("scrolling");
	}
});
$("#navbarResponsive").on("hide.bs.collapse", () => {
	if (!isScrolling && topBar.hasClass("hero")) {
		topBar.removeClass("scrolling");
	}
});

function popupCenter(url: string, title: string, w: number, h: number) {
    // Fixes dual-screen position                         Most browsers      Firefox
    const dualScreenLeft = window.screenLeft != undefined ? window.screenLeft : (<any> screen)["left"];
    const dualScreenTop = window.screenTop != undefined ? window.screenTop : (<any>screen)["top"];

    const width = window.innerWidth ? window.innerWidth : document.documentElement.clientWidth ? document.documentElement.clientWidth : screen.width;
    const height = window.innerHeight ? window.innerHeight : document.documentElement.clientHeight ? document.documentElement.clientHeight : screen.height;

    const left = ((width / 2) - (w / 2)) + dualScreenLeft;
    const top = ((height / 2) - (h / 2)) + dualScreenTop;
    const newWindow = window.open(url, title, 'scrollbars=yes, width=' + w + ', height=' + h + ', top=' + top + ', left=' + left);

    // Puts focus on the newWindow
    if (window.focus) {
        newWindow.focus();
    }
}

function share(e: Event) {
    e.preventDefault();
    popupCenter((<HTMLAnchorElement> e.currentTarget).href, "Share", 1024, 768);
}

$('.nav-link.social').on("click", share);
$('.social-buttons.hidden-xs-down .social-button').on("click", share);
$('.social-inline').on("click", share);
