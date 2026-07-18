window.addEventListener('load', CheckSidebar);
window.addEventListener('resize', CheckSidebar);

function CheckSidebar() {
    var sidebar = document.querySelector('.sidebar');
    if (!sidebar) {
        return;
    }

    if (window.innerWidth >= 1200) {
        setHidden('.sidebar-hide-button', true);
        sidebar.hidden = false;
        if (sidebar.classList.contains('sidebar-mobile')) {
            sidebar.classList.remove('sidebar-mobile');
            setHidden('.content-area', false);
            setHidden('.footer', false);
        }
        return;
    }

    sidebar.hidden = true;
    setHidden('.content-area', false);
}

function SidebarOpen() {
    var sidebar = document.querySelector('.sidebar');
    sidebar?.classList.add('sidebar-mobile');
    if (sidebar) {
        sidebar.hidden = false;
    }
    setHidden('.sidebar-hide-button', false);
    setHidden('.content-area', true);
    setHidden('.footer', true);
}

function SidebarClose() {
    var sidebar = document.querySelector('.sidebar');
    sidebar?.classList.remove('sidebar-mobile');
    if (sidebar) {
        sidebar.hidden = true;
    }
    setHidden('.sidebar-hide-button', true);
    setHidden('.content-area', false);
    setHidden('.footer', false);
}

function setHidden(selector, hidden) {
    document.querySelectorAll(selector).forEach(function (element) {
        element.hidden = hidden;
    });
}

window.CheckSidebar = CheckSidebar;
window.SidebarOpen = SidebarOpen;
window.SidebarClose = SidebarClose;
