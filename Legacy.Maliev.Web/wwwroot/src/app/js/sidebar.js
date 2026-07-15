window.onload = function () {
    CheckSidebar();
};

window.onresize = function () {
    CheckSidebar();
};

function CheckSidebar() {
    if ($(window).outerWidth() >= 1200) {
        if ($('.sidebar-hide-button').is(':visible')) {
            $('.sidebar-hide-button').hide();
        }

        if ($('.sidebar').is(':hidden')) {
            $('.sidebar').show();
        }

        if ($('.sidebar').hasClass('sidebar-mobile')) {
            $('.sidebar').removeClass('sidebar-mobile');
            $('.content-area').show();
            $('.footer').show();
        }
    }
    else {
        $('.sidebar').hide();
        $('.content-area').show();
    }
}

function SidebarOpen() {
    $('.sidebar').addClass('sidebar-mobile');
    $('.sidebar').show();
    $('.sidebar-hide-button').show();
    $('.content-area').hide();
    $('.footer').hide();
}

function SidebarClose() {
    $('.sidebar').removeClass('sidebar-mobile');
    $('.sidebar').hide();
    $('.sidebar-hide-button').hide();
    $('.content-area').show();
    $('.footer').show();
}