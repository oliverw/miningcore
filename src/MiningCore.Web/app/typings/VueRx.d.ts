import { WatchOptions } from "vue/types/options";
import * as Rx from "rxjs/Rx";

declare module "vue/types/vue" {
	interface Vue {
		$watchAsObservable<T>(expOrFn: string | Function, options?: WatchOptions): Rx.Observable<{ oldValue: T, newValue: T}>;
		_disposables: Rx.Subscription;
	}
}
