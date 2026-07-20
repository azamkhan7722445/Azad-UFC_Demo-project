#import "UnityAppController.h"

// Forward declarations for the plugin functions
extern "C" void UnityPluginLoad(IUnityInterfaces* unityInterfaces);
extern "C" void UnityPluginUnload(void);

@interface GraciaPluginController : UnityAppController
@end

@implementation GraciaPluginController

- (void)shouldAttachRenderDelegate {
    [super shouldAttachRenderDelegate];
    UnityRegisterRenderingPluginV5(&UnityPluginLoad, &UnityPluginUnload);
}

@end

IMPL_APP_CONTROLLER_SUBCLASS(GraciaPluginController)


