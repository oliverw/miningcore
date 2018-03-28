#include <nan.h>
#include <node.h>
#include <node_buffer.h>
#include <v8.h>
#include <stdint.h>
#include "crypto/equihash.h"


#include <vector>
using namespace v8;

int verifyEH(const char *hdr, const std::vector<unsigned char> &soln){
  unsigned int n = 200;
  unsigned int k = 9;

  // Hash state
  crypto_generichash_blake2b_state state;
  EhInitialiseState(n, k, state);

  crypto_generichash_blake2b_update(&state, (const unsigned char*)hdr, 140);

  bool isValid = Eh200_9.IsValidSolution(state, soln);
  
  return isValid;
}

void Verify(const v8::FunctionCallbackInfo<Value>& args) {
  Isolate* isolate = Isolate::GetCurrent();
  HandleScope scope(isolate);

  if (args.Length() < 2) {
  isolate->ThrowException(Exception::TypeError(
    String::NewFromUtf8(isolate, "Wrong number of arguments")));
  return;
  }

  Local<Object> header = args[0]->ToObject();
  Local<Object> solution = args[1]->ToObject();

  if(!node::Buffer::HasInstance(header) || !node::Buffer::HasInstance(solution)) {
  isolate->ThrowException(Exception::TypeError(
    String::NewFromUtf8(isolate, "Arguments should be buffer objects.")));
  return;
  }

  const char *hdr = node::Buffer::Data(header);
  if(node::Buffer::Length(header) != 140) {
	  //invalid hdr length
	  args.GetReturnValue().Set(false);
	  return;
  }
  const char *soln = node::Buffer::Data(solution);

  std::vector<unsigned char> vecSolution(soln, soln + node::Buffer::Length(solution));

  bool result = verifyEH(hdr, vecSolution);
  args.GetReturnValue().Set(result);

}


void Init(Handle<Object> exports) {
  NODE_SET_METHOD(exports, "verify", Verify);
}

NODE_MODULE(equihashverify, Init)
